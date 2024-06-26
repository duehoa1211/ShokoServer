using System;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Extensions.Logging;
using Shoko.Server.Providers.AniDB.Interfaces;
using Shoko.Server.Providers.AniDB.UDP.Connection;
using Shoko.Server.Providers.AniDB.UDP.Exceptions;
using Shoko.Server.Providers.AniDB.UDP.Generic;
using Shoko.Server.Server;
using Shoko.Server.Settings;
using Shoko.Server.Utilities;

namespace Shoko.Server.Providers.AniDB.UDP;

public class AniDBUDPConnectionHandler : ConnectionHandler, IUDPConnectionHandler
{
    private readonly IRequestFactory _requestFactory;
    private IAniDBSocketHandler _socketHandler;
    private static readonly Regex s_logMask = new("(?<=(\\bpass=|&pass=\\bs=|&s=))[^&]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public event EventHandler LoginFailed;

    public override double BanTimerResetLength => 1.5D;
    public override string Type => "UDP";
    public override UpdateType BanEnum => UpdateType.UDPBan;

    public string SessionID { get; private set; }
    public bool IsAlive { get; private set; }

    private string _cdnDomain = Constants.URLS.AniDB_Images_Domain;

    public string ImageServerUrl => string.Format(Constants.URLS.AniDB_Images, _cdnDomain);

    private ISettingsProvider SettingsProvider { get; set; }

    private Timer _pulseTimer;

    private bool _isInvalidSession;

    public bool IsInvalidSession
    {
        get => _isInvalidSession;

        set
        {
            _isInvalidSession = value;
            UpdateState(new AniDBStateUpdate
            {
                UpdateType = UpdateType.InvalidSession, UpdateTime = DateTime.Now, Value = value
            });
        }
    }

    private bool _isLoggedOn;

    public bool IsLoggedOn
    {
        get => _isLoggedOn;
        set => _isLoggedOn = value;
    }

    public bool IsNetworkAvailable { private set; get; }

    private DateTime LastAniDBPing { get; set; } = DateTime.MinValue;

    private DateTime LastAniDBMessageNonPing { get; set; } = DateTime.MinValue;

    private DateTime LastMessage =>
        LastAniDBMessageNonPing < LastAniDBPing ? LastAniDBPing : LastAniDBMessageNonPing;

    public AniDBUDPConnectionHandler(IRequestFactory requestFactory, ILoggerFactory loggerFactory,
        ISettingsProvider settings, UDPRateLimiter rateLimiter) : base(loggerFactory, rateLimiter)
    {
        _requestFactory = requestFactory;
        SettingsProvider = settings;
    }

    ~AniDBUDPConnectionHandler()
    {
        Logger.LogInformation("Disposing AniDBUDPConnectionHandler...");
        CloseConnections().GetAwaiter().GetResult();
    }

    public new void ExtendBanTimer(int time, string message)
    {
        base.ExtendBanTimer(time, message);
    }

    public async Task<bool> Init()
    {
        var settings = SettingsProvider.GetSettings();
        if (!ValidAniDBCredentials(settings.AniDb.Username, settings.AniDb.Password)) return false;
        await InitInternal();
        return true;
    }
    
    public async Task<bool> Init(string username, string password, string serverName, ushort serverPort, ushort clientPort)
    {
        var settings = SettingsProvider.GetSettings();
        settings.AniDb.ServerAddress = serverName;
        settings.AniDb.ServerPort = serverPort;
        settings.AniDb.ClientPort = clientPort;

        if (!ValidAniDBCredentials(username, password)) return false;

        SetCredentials(username, password);
        await InitInternal();
        return true;
    }

    private async Task InitInternal()
    {
        if (_socketHandler != null)
        {
            await _socketHandler.DisposeAsync();
            _socketHandler = null;
        }

        var settings = SettingsProvider.GetSettings();
        ArgumentNullException.ThrowIfNull(settings.AniDb?.ServerAddress);
        if (settings.AniDb.ServerPort <= 0) throw new ArgumentException("AniDB Server Port is invalid");
        if (settings.AniDb.ClientPort <= 0) throw new ArgumentException("AniDB Client Port is invalid");
        _socketHandler = new AniDBSocketHandler(_loggerFactory, settings.AniDb.ServerAddress, settings.AniDb.ServerPort,
            settings.AniDb.ClientPort);
        _isLoggedOn = false;

        IsNetworkAvailable = await _socketHandler.TryConnection();

        _pulseTimer = new Timer { Interval = 5000, Enabled = true, AutoReset = true };
        _pulseTimer.Elapsed += PulseTimerElapsed;

        Logger.LogInformation("starting ping timer...");
        _pulseTimer.Start();
        IsAlive = true;
    }

    private void PulseTimerElapsed(object sender, ElapsedEventArgs e)
    {
        try
        {
            var tempTimestamp = DateTime.Now - LastMessage;
            if (ExtendPauseSecs.HasValue && tempTimestamp.TotalSeconds >= ExtendPauseSecs.Value)
            {
                ResetBanTimer();
            }

            if (!_isLoggedOn)
            {
                return;
            }

            // don't ping when AniDB is taking a long time to respond
            if (_socketHandler.IsLocked) return;

            var nonPingTimestamp = DateTime.Now - LastAniDBMessageNonPing;
            var pingTimestamp = DateTime.Now - LastAniDBPing;
            tempTimestamp = DateTime.Now - LastMessage;

            // if we haven't sent a command for 45 seconds, send a ping just to keep the connection alive
            if (tempTimestamp.TotalSeconds >= Constants.PingFrequency &&
                pingTimestamp.TotalSeconds >= Constants.PingFrequency &&
                !IsBanned && !ExtendPauseSecs.HasValue)
            {
                var ping = _requestFactory.Create<RequestPing>();
                ping.Send();
            }

            if (nonPingTimestamp.TotalSeconds > Constants.ForceLogoutPeriod) // after 10 minutes
                ForceLogout();
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "{Message}", exception);
        }
    }

    /// <summary>
    /// Actually get data from AniDB
    /// </summary>
    /// <param name="command">The request to be made (AUTH user=baka&amp;pass....)</param>
    /// <param name="needsUnicode">Only for Login, specify whether to ask for UTF16</param>
    /// <param name="isPing">is it a ping command</param>
    /// <returns></returns>
    public async Task<string> CallAniDBUDP(string command, bool needsUnicode = true, bool isPing = false)
    {
        // Steps:
        // 1. Check Ban state and throw if Banned
        // 2. Check Login State and Login if needed
        // 3. Actually Call AniDB

        if (_socketHandler == null) throw new ObjectDisposedException("The connection was closed by shoko before this request was made");
        // Check Ban State
        // Ideally, this will never happen, as we stop the queue and attempt a graceful rollback of the command
        if (IsBanned)
        {
            throw new AniDBBannedException
            {
                BanType = UpdateType.UDPBan, BanExpires = BanTime?.AddHours(BanTimerResetLength)
            };
        }
        // TODO Low Priority: We need to handle Login Attempt Decay, so that we can try again if it's not just a bad user/pass
        // It wasn't handled before, and it's not caused serious problems

        // if we got here and it's invalid session, then it already failed to re-log
        if (IsInvalidSession)
        {
            throw new NotLoggedInException();
        }

        // Check Login State
        if (!await Login())
        {
            throw new NotLoggedInException();
        }

        // Actually Call AniDB
        return await CallAniDBUDPDirectly(command, needsUnicode, isPing);
    }

    public async Task<string> CallAniDBUDPDirectly(string command, bool needsUnicode = true, bool isPing = false)
    {
        // 1. Call AniDB
        // 2. Decode the response, converting Unicode and decompressing, as needed
        // 3. Check for an Error Response
        // 4. Return a pretty response object, with a parsed return code and trimmed string
        var encoding = needsUnicode ? new UnicodeEncoding(true, false) : Encoding.ASCII;

        RateLimiter.EnsureRate();
        if (_socketHandler == null) throw new ObjectDisposedException("The connection was closed by shoko");
        var start = DateTime.Now;

        Logger.LogTrace("AniDB UDP Call: (Using {Unicode}) {Command}", needsUnicode ? "Unicode" : "ASCII", MaskLog(command));

        var sendByteAdd = encoding.GetBytes(command);
        StampLastMessage(isPing);
        var byReceivedAdd = await _socketHandler.Send(sendByteAdd);
        StampLastMessage(isPing);

        if (byReceivedAdd.All(a => a == 0))
        {
            // we are probably banned or have lost connection. We can't tell the difference, so we're assuming ban
            IsBanned = true;
            throw new AniDBBannedException
            {
                BanType = UpdateType.UDPBan, BanExpires = BanTime?.AddHours(BanTimerResetLength)
            };
        }

        // decode
        var decodedString = Utils.GetEncoding(byReceivedAdd).GetString(byReceivedAdd, 0, byReceivedAdd.Length);
        if (decodedString[0] == 0xFEFF) // remove BOM
        {
            decodedString = decodedString[1..];
        }

        var ts = DateTime.Now - start;
        Logger.LogTrace("AniDB Response: Received in {Time:ss'.'ffff}s\n{DecodedString}", ts, MaskLog(decodedString));

        return decodedString;
    }

    public async Task ForceReconnection()
    {
        try
        {
            ForceLogout();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to logout");
        }

        try
        {
            await CloseConnections();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to close socket");
        }

        try
        {
            await InitInternal();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to reinitialize socket");
        }
    }

    private void StampLastMessage(bool isPing)
    {
        if (isPing)
        {
            LastAniDBPing = DateTime.Now;
        }
        else
        {
            LastAniDBMessageNonPing = DateTime.Now;
        }
    }

    public void ForceLogout()
    {
        if (!_isLoggedOn)
        {
            return;
        }

        if (IsBanned)
        {
            _isLoggedOn = false;
            SessionID = null;
            return;
        }

        Logger.LogTrace("Logging Out");
        try
        {
            _requestFactory.Create<RequestLogout>().Send();
        }
        catch
        {
            // ignore
        }

        _isLoggedOn = false;
        SessionID = null;
    }

    public async Task CloseConnections()
    {
        IsNetworkAvailable = false;
        IsAlive = false;
        _pulseTimer?.Stop();
        _pulseTimer = null;
        if (_socketHandler == null) return;

        Logger.LogInformation("AniDB UDP Socket Disposing...");
        await _socketHandler.DisposeAsync();
        _socketHandler = null;
    }

    public async Task<bool> Login()
    {
        var settings = SettingsProvider.GetSettings();
        if (await Login(settings.AniDb.Username, settings.AniDb.Password)) return true;

        try
        {
            Logger.LogTrace("Failed to login to AniDB. Issuing a Logout command and retrying");
            ForceLogout();
            return await Login(settings.AniDb.Username, settings.AniDb.Password);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "{Message}", e);
        }

        return false;
    }

    private async Task<bool> Login(string username, string password)
    {
        // check if we are already logged in
        if (IsLoggedOn) return true;

        if (!ValidAniDBCredentials(username, password))
        {
            LoginFailed?.Invoke(this, null!);
            return false;
        }

        Logger.LogTrace("Logging in");
        UDPResponse<ResponseLogin> response;
        try
        {
            response = await LoginWithFallbacks(username, password);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Unable to login to AniDB");
            response = new UDPResponse<ResponseLogin>();
        }

        switch (response.Code)
        {
            case UDPReturnCode.LOGIN_FAILED:
                SessionID = null;
                IsInvalidSession = true;
                IsLoggedOn = false;
                Logger.LogError("AniDB Login Failed: invalid credentials");
                LoginFailed?.Invoke(this, null!);
                break;
            case UDPReturnCode.LOGIN_ACCEPTED:
                SessionID = response.Response.SessionID;
                _cdnDomain = response.Response.ImageServer;
                IsLoggedOn = true;
                IsInvalidSession = false;
                return true;
            default:
                SessionID = null;
                IsLoggedOn = false;
                IsInvalidSession = true;
                break;
        }

        return false;
    }

    private async Task<UDPResponse<ResponseLogin>> LoginWithFallbacks(string username, string password)
    {
        try
        {
            var login = _requestFactory.Create<RequestLogin>(
                r =>
                {
                    r.Username = username;
                    r.Password = password;
                }
            );
            return login.Send();
        }
        catch (UnexpectedUDPResponseException)
        {
            Logger.LogTrace(
                "Received an UnexpectedUDPResponseException on Login. This usually happens because of an unexpected shutdown. Relogging using Unicode");
            var login = _requestFactory.Create<RequestLogin>(
                r =>
                {
                    r.Username = username;
                    r.Password = password;
                    r.UseUnicode = true;
                }
            );
            return login.Send();
        }
        catch (SocketException e)
            when (e.SocketErrorCode == SocketError.TimedOut)
        {
            Logger.LogTrace("Received a Timeout on Login. Restarting Socket and relogging");
            await ForceReconnection();
            var login = _requestFactory.Create<RequestLogin>(
                r =>
                {
                    r.Username = username;
                    r.Password = password;
                    r.UseUnicode = true;
                }
            );
            return login.Send();
        }
        catch (SocketException e)
        {
            Logger.LogError(e, "Unable to login to AniDB");
            return new UDPResponse<ResponseLogin>();
        }
    }

    public async Task<bool> TestLogin(string username, string password)
    {
        if (!ValidAniDBCredentials(username, password))
        {
            return false;
        }

        var result = await Login(username, password);
        if (result)
        {
            ForceLogout();
        }

        return result;
    }

    public bool SetCredentials(string username, string password)
    {
        if (!ValidAniDBCredentials(username, password))
        {
            return false;
        }

        var settings = SettingsProvider.GetSettings();
        settings.AniDb.Username = username;
        settings.AniDb.Password = password;
        SettingsProvider.SaveSettings();
        return true;
    }

    public bool ValidAniDBCredentials(string user, string pass)
    {
        if (string.IsNullOrEmpty(user)) return false;
        if (string.IsNullOrEmpty(pass)) return false;

        return true;
    }

    private static string MaskLog(string input)
    {
        return s_logMask.Replace(input, "****");
    }
}
