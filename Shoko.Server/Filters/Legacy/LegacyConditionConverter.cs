using System;
using System.Collections.Generic;
using System.Linq;
using Shoko.Models.Enums;
using Shoko.Models.Server;
using Shoko.Server.Filters.Files;
using Shoko.Server.Filters.Functions;
using Shoko.Server.Filters.Info;
using Shoko.Server.Filters.Interfaces;
using Shoko.Server.Filters.Logic;
using Shoko.Server.Filters.Logic.DateTimes;
using Shoko.Server.Filters.Logic.Numbers;
using Shoko.Server.Filters.Selectors;
using Shoko.Server.Filters.SortingSelectors;
using Shoko.Server.Filters.User;
using Shoko.Server.Models;

namespace Shoko.Server.Filters.Legacy;

public static class LegacyConditionConverter
{
    public static bool TryConvertToConditions(FilterPreset filter, out List<GroupFilterCondition> conditions, out GroupFilterBaseCondition baseCondition)
    {
        // The allowed conversions are:
        // Not(...) -> BaseCondition Inverted
        // And(And(And(...))) -> Chains of And become the list
        // a single condition

        var expression = filter.Expression;
        // treat null expression similar to All
        if (expression == null)
        {
            conditions = new List<GroupFilterCondition>();
            baseCondition = GroupFilterBaseCondition.Include;
            return true;
        }

        conditions = null;
        var results = new List<GroupFilterCondition>();
        if (expression is NotExpression not)
        {
            baseCondition = GroupFilterBaseCondition.Exclude;
            if (!TryGetConditionsRecursive(not.Left, results)) return false;

            conditions = results;
            return true;
        }

        baseCondition = GroupFilterBaseCondition.Include;
        if (!TryGetConditionsRecursive(expression, results)) return false;
        conditions = results;
        return true;
    }

    public static bool TryGetConditionsRecursive(FilterExpression expression, List<GroupFilterCondition> conditions)
    {
        if (expression is AndExpression and) return TryGetConditionsRecursive(and.Left, conditions) && TryGetConditionsRecursive(and.Right, conditions);

        if (!TryGetCondition(expression, out var condition)) return false;
        conditions.Add(condition);
        return true;
    }

    private static bool TryGetCondition(FilterExpression expression, out GroupFilterCondition condition)
    {
        if (expression is NotExpression not && TryGetIncludeCondition(not.Left, out condition))
        {
            condition.ConditionOperator = (int)GroupFilterOperator.Exclude;
            return true;
        }

        if (TryGetIncludeCondition(expression, out condition)) return true;
        if (TryGetInCondition(expression, out condition)) return true;
        if (TryGetComparatorCondition(expression, out condition)) return true;
        return false;
    }

    private static bool TryGetIncludeCondition(FilterExpression expression, out GroupFilterCondition condition)
    {
        condition = null;
        var type = expression.GetType();
        if (type == typeof(HasMissingEpisodesExpression))
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = (int)GroupFilterOperator.Include, ConditionType = (int)GroupFilterConditionType.MissingEpisodes
            };
            return true;
        }

        if (type == typeof(HasMissingEpisodesCollectingExpression))
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = (int)GroupFilterOperator.Include, ConditionType = (int)GroupFilterConditionType.MissingEpisodesCollecting
            };
            return true;
        }

        if (type == typeof(HasUnwatchedEpisodesExpression))
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = (int)GroupFilterOperator.Include, ConditionType = (int)GroupFilterConditionType.HasUnwatchedEpisodes
            };
            return true;
        }

        if (type == typeof(HasWatchedEpisodesExpression))
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = (int)GroupFilterOperator.Include, ConditionType = (int)GroupFilterConditionType.HasWatchedEpisodes
            };
            return true;
        }

        if (type == typeof(HasPermanentUserVotesExpression))
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = (int)GroupFilterOperator.Include, ConditionType = (int)GroupFilterConditionType.UserVoted
            };
            return true;
        }

        if (type == typeof(HasUserVotesExpression))
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = (int)GroupFilterOperator.Include, ConditionType = (int)GroupFilterConditionType.UserVotedAny
            };
            return true;
        }

        if (type == typeof(HasTvDBLinkExpression))
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = (int)GroupFilterOperator.Include, ConditionType = (int)GroupFilterConditionType.AssignedTvDBInfo
            };
            return true;
        }

        if (type == typeof(HasTMDbLinkExpression))
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = (int)GroupFilterOperator.Include, ConditionType = (int)GroupFilterConditionType.AssignedMovieDBInfo
            };
            return true;
        }

        if (type == typeof(HasTraktLinkExpression))
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = (int)GroupFilterOperator.Include, ConditionType = (int)GroupFilterConditionType.AssignedTraktInfo
            };
            return true;
        }

        if (type == typeof(IsFavoriteExpression))
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = (int)GroupFilterOperator.Include, ConditionType = (int)GroupFilterConditionType.Favourite
            };
            return true;
        }

        if (type == typeof(IsFinishedExpression))
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = (int)GroupFilterOperator.Include, ConditionType = (int)GroupFilterConditionType.FinishedAiring
            };
            return true;
        }

        if (type == typeof(HasTMDbLinkExpression))
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = (int)GroupFilterOperator.Include, ConditionType = (int)GroupFilterConditionType.AssignedMovieDBInfo
            };
            return true;
        }

        if (expression == LegacyMappings.GetCompletedExpression())
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = (int)GroupFilterOperator.Include, ConditionType = (int)GroupFilterConditionType.CompletedSeries
            };
            return true;
        }

        if (expression == new OrExpression(new HasTvDBLinkExpression(), new HasTMDbLinkExpression()))
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = (int)GroupFilterOperator.Include, ConditionType = (int)GroupFilterConditionType.AssignedTvDBOrMovieDBInfo
            };
            return true;
        }

        return false;
    }

    private static bool TryGetInCondition(FilterExpression expression, out GroupFilterCondition condition)
    {
        condition = null;

        if (IsInTag(expression, out var tags, out var inverted))
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = inverted ? (int)GroupFilterOperator.NotIn : (int)GroupFilterOperator.In,
                ConditionType = (int)GroupFilterConditionType.Tag,
                ConditionParameter = string.Join(",", tags)
            };
            return true;
        }

        if (IsInCustomTag(expression, out var customTags, out inverted))
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = inverted ? (int)GroupFilterOperator.NotIn : (int)GroupFilterOperator.In,
                ConditionType = (int)GroupFilterConditionType.CustomTags,
                ConditionParameter = string.Join(",", customTags)
            };
            return true;
        }

        if (IsInAnimeType(expression, out var animeType, out inverted))
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = inverted ? (int)GroupFilterOperator.NotIn : (int)GroupFilterOperator.In,
                ConditionType = (int)GroupFilterConditionType.AnimeType,
                ConditionParameter = string.Join(",", animeType)
            };
            return true;
        }

        if (IsInVideoQuality(expression, out var videoQualities, out inverted))
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = inverted ? (int)GroupFilterOperator.NotIn : (int)GroupFilterOperator.In,
                ConditionType = (int)GroupFilterConditionType.VideoQuality,
                ConditionParameter = string.Join(",", videoQualities)
            };
            return true;
        }

        if (IsInGroup(expression, out var groups, out inverted))
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = inverted ? (int)GroupFilterOperator.NotIn : (int)GroupFilterOperator.In,
                ConditionType = (int)GroupFilterConditionType.AnimeGroup,
                ConditionParameter = string.Join(",", groups)
            };
            return true;
        }

        if (IsInYear(expression, out var years, out inverted))
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = inverted ? (int)GroupFilterOperator.NotIn : (int)GroupFilterOperator.In,
                ConditionType = (int)GroupFilterConditionType.Year,
                ConditionParameter = string.Join(",", years)
            };
            return true;
        }

        if (IsInSeason(expression, out var seasons, out inverted))
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = inverted ? (int)GroupFilterOperator.NotIn : (int)GroupFilterOperator.In,
                ConditionType = (int)GroupFilterConditionType.Season,
                ConditionParameter = string.Join(",", seasons.Select(a => a.Season + " " + a.Year))
            };
            return true;
        }

        if (IsInAudioLanguage(expression, out var aLanguages, out inverted))
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = inverted ? (int)GroupFilterOperator.NotIn : (int)GroupFilterOperator.In,
                ConditionType = (int)GroupFilterConditionType.AudioLanguage,
                ConditionParameter = string.Join(",", aLanguages)
            };
            return true;
        }

        if (IsInSubtitleLanguage(expression, out var sLanguages, out inverted))
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = inverted ? (int)GroupFilterOperator.NotIn : (int)GroupFilterOperator.In,
                ConditionType = (int)GroupFilterConditionType.SubtitleLanguage,
                ConditionParameter = string.Join(",", sLanguages)
            };
            return true;
        }

        if (IsInSharedVideoQuality(expression, out var sVideoQuality, out inverted))
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = inverted ? (int)GroupFilterOperator.NotInAllEpisodes : (int)GroupFilterOperator.InAllEpisodes,
                ConditionType = (int)GroupFilterConditionType.VideoQuality,
                ConditionParameter = string.Join(",", sVideoQuality)
            };
            return true;
        }

        if (IsInSharedAudioLanguage(expression, out var sALanguages, out inverted))
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = inverted ? (int)GroupFilterOperator.NotInAllEpisodes : (int)GroupFilterOperator.InAllEpisodes,
                ConditionType = (int)GroupFilterConditionType.AudioLanguage,
                ConditionParameter = string.Join(",", sALanguages)
            };
            return true;
        }

        if (IsInSharedSubtitleLanguage(expression, out var sSLanguages, out inverted))
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = inverted ? (int)GroupFilterOperator.NotInAllEpisodes : (int)GroupFilterOperator.InAllEpisodes,
                ConditionType = (int)GroupFilterConditionType.SubtitleLanguage,
                ConditionParameter = string.Join(",", sSLanguages)
            };
            return true;
        }

        return false;
    }

    private static bool TryGetComparatorCondition(FilterExpression expression, out GroupFilterCondition condition)
    {
        condition = null;
        if (IsAirDate(expression, out var airDatePara, out var airDateOperator))
        {
            var para = airDatePara is DateTime date ? date.ToString("yyyyMMdd") : airDatePara.ToString();
            condition = new GroupFilterCondition
            {
                ConditionOperator = (int)airDateOperator,
                ConditionType = (int)GroupFilterConditionType.AirDate,
                ConditionParameter = para
            };
            return true;
        }

        if (IsLatestAirDate(expression, out var lastAirDatePara, out var lastAirDateOperator))
        {
            var para = lastAirDatePara is DateTime date ? date.ToString("yyyyMMdd") : lastAirDatePara.ToString();
            condition = new GroupFilterCondition
            {
                ConditionOperator = (int)lastAirDateOperator,
                ConditionType = (int)GroupFilterConditionType.LatestEpisodeAirDate,
                ConditionParameter = para
            };
            return true;
        }

        if (IsSeriesCreatedDate(expression, out var seriesCreatedDatePara, out var seriesCreatedDateOperator))
        {
            var para = seriesCreatedDatePara is DateTime date ? date.ToString("yyyyMMdd") : seriesCreatedDatePara.ToString();
            condition = new GroupFilterCondition
            {
                ConditionOperator = (int)seriesCreatedDateOperator,
                ConditionType = (int)GroupFilterConditionType.SeriesCreatedDate,
                ConditionParameter = para
            };
            return true;
        }

        if (IsEpisodeAddedDate(expression, out var episodeAddedDatePara, out var episodeAddedDateOperator))
        {
            var para = episodeAddedDatePara is DateTime date ? date.ToString("yyyyMMdd") : episodeAddedDatePara.ToString();
            condition = new GroupFilterCondition
            {
                ConditionOperator = (int)episodeAddedDateOperator,
                ConditionType = (int)GroupFilterConditionType.EpisodeAddedDate,
                ConditionParameter = para
            };
            return true;
        }

        if (IsEpisodeWatchedDate(expression, out var episodeWatchedDatePara, out var episodeWatchedDateOperator))
        {
            var para = episodeWatchedDatePara is DateTime date ? date.ToString("yyyyMMdd") : episodeWatchedDatePara.ToString();
            condition = new GroupFilterCondition
            {
                ConditionOperator = (int)episodeWatchedDateOperator,
                ConditionType = (int)GroupFilterConditionType.EpisodeWatchedDate,
                ConditionParameter = para
            };
            return true;
        }

        if (IsAniDBRating(expression, out var aniDBRatingPara, out var aniDBRatingOperator))
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = (int)aniDBRatingOperator,
                ConditionType = (int)GroupFilterConditionType.AniDBRating,
                ConditionParameter = aniDBRatingPara.ToString()
            };
            return true;
        }

        if (IsUserRating(expression, out var userRatingPara, out var userRatingOperator))
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = (int)userRatingOperator,
                ConditionType = (int)GroupFilterConditionType.UserRating,
                ConditionParameter = userRatingPara.ToString()
            };
            return true;
        }

        if (IsEpisodeCount(expression, out var episodeCountPara, out var episodeCountOperator))
        {
            condition = new GroupFilterCondition
            {
                ConditionOperator = (int)episodeCountOperator,
                ConditionType = (int)GroupFilterConditionType.EpisodeCount,
                ConditionParameter = episodeCountPara.ToString()
            };
            return true;
        }

        return false;
    }

    private static bool IsInTag(FilterExpression expression, out List<string> parameters, out bool inverted)
    {
        parameters = new List<string>();
        return TryParseIn(expression, typeof(HasTagExpression), parameters, out inverted);
    }

    private static bool IsInCustomTag(FilterExpression expression, out List<string> parameters, out bool inverted)
    {
        parameters = new List<string>();
        return TryParseIn(expression, typeof(HasTagExpression), parameters, out inverted);
    }

    private static bool IsInAnimeType(FilterExpression expression, out List<string> parameters, out bool inverted)
    {
        parameters = new List<string>();
        return TryParseIn(expression, typeof(HasAnimeTypeExpression), parameters, out inverted);
    }

    private static bool IsInVideoQuality(FilterExpression expression, out List<string> parameters, out bool inverted)
    {
        parameters = new List<string>();
        return TryParseIn(expression, typeof(HasVideoSourceExpression), parameters, out inverted);
    }

    private static bool IsInSharedVideoQuality(FilterExpression expression, out List<string> parameters, out bool inverted)
    {
        parameters = new List<string>();
        return TryParseIn(expression, typeof(HasSharedVideoSourceExpression), parameters, out inverted);
    }

    private static bool IsInGroup(FilterExpression expression, out List<string> parameters, out bool inverted)
    {
        parameters = new List<string>();
        return TryParseIn(expression, typeof(HasNameExpression), parameters, out inverted);
    }

    private static bool IsInYear(FilterExpression expression, out List<int> parameters, out bool inverted)
    {
        parameters = new List<int>();
        return TryParseIn(expression, typeof(InYearExpression), parameters, out inverted);
    }

    private static bool IsInSeason(FilterExpression expression, out List<(int Year, string Season)> parameters, out bool inverted)
    {
        parameters = new List<(int, string)>();
        return TryParseIn(expression, typeof(InSeasonExpression), parameters, out inverted);
    }

    private static bool IsInAudioLanguage(FilterExpression expression, out List<string> parameters, out bool inverted)
    {
        parameters = new List<string>();
        return TryParseIn(expression, typeof(HasAudioLanguageExpression), parameters, out inverted);
    }

    private static bool IsInSubtitleLanguage(FilterExpression expression, out List<string> parameters, out bool inverted)
    {
        parameters = new List<string>();
        return TryParseIn(expression, typeof(HasSubtitleLanguageExpression), parameters, out inverted);
    }

    private static bool IsInSharedAudioLanguage(FilterExpression expression, out List<string> parameters, out bool inverted)
    {
        parameters = new List<string>();
        return TryParseIn(expression, typeof(HasSharedAudioLanguageExpression), parameters, out inverted);
    }

    private static bool IsInSharedSubtitleLanguage(FilterExpression expression, out List<string> parameters, out bool inverted)
    {
        parameters = new List<string>();
        return TryParseIn(expression, typeof(HasSharedSubtitleLanguageExpression), parameters, out inverted);
    }

    private static bool IsAirDate(FilterExpression expression, out object parameter, out GroupFilterOperator gfOperator)
    {
        return TryParseComparator(expression, typeof(AirDateSelector), out parameter, out gfOperator);
    }

    private static bool IsLatestAirDate(FilterExpression expression, out object parameter, out GroupFilterOperator gfOperator)
    {
        return TryParseComparator(expression, typeof(LastAirDateSelector), out parameter, out gfOperator);
    }

    private static bool IsSeriesCreatedDate(FilterExpression expression, out object parameter, out GroupFilterOperator gfOperator)
    {
        return TryParseComparator(expression, typeof(AddedDateSelector), out parameter, out gfOperator);
    }

    private static bool IsEpisodeAddedDate(FilterExpression expression, out object parameter, out GroupFilterOperator gfOperator)
    {
        return TryParseComparator(expression, typeof(LastAddedDateSelector), out parameter, out gfOperator);
    }

    private static bool IsEpisodeWatchedDate(FilterExpression expression, out object parameter, out GroupFilterOperator gfOperator)
    {
        return TryParseComparator(expression, typeof(LastWatchedDateSelector), out parameter, out gfOperator);
    }

    private static bool IsAniDBRating(FilterExpression expression, out object parameter, out GroupFilterOperator gfOperator)
    {
        return TryParseComparator(expression, typeof(HighestAniDBRatingSelector), out parameter, out gfOperator);
    }

    private static bool IsUserRating(FilterExpression expression, out object parameter, out GroupFilterOperator gfOperator)
    {
        return TryParseComparator(expression, typeof(HighestUserRatingSelector), out parameter, out gfOperator);
    }

    private static bool IsEpisodeCount(FilterExpression expression, out object parameter, out GroupFilterOperator gfOperator)
    {
        return TryParseComparator(expression, typeof(EpisodeCountSelector), out parameter, out gfOperator);
    }

    private static bool TryParseIn<T>(FilterExpression expression, Type type, List<T> parameters, out bool inverted)
    {
        inverted = false;
        if (expression is NotExpression not)
        {
            inverted = true;
            expression = not.Left;
        }

        if (expression is OrExpression or) return TryParseIn(or.Left, type, parameters, out _) && TryParseIn(or.Right, type, parameters, out _);
        if (expression.GetType() != type) return false;

        if (typeof(T) == typeof(string) && expression is IWithStringParameter withString) parameters.Add((T)(object)withString.Parameter);
        else if (typeof(T) == typeof(DateTime) && expression is IWithDateParameter withDate) parameters.Add((T)(object)withDate.Parameter);
        else if (typeof(T) == typeof(double) && expression is IWithNumberParameter withNumber) parameters.Add((T)(object)withNumber.Parameter);
        else if (typeof(T) == typeof(TimeSpan) && expression is IWithTimeSpanParameter withTimeSpan) parameters.Add((T)(object)withTimeSpan.Parameter);

        return true;

    }

    private static bool TryParseIn<T,T1>(FilterExpression expression, Type type, List<(T, T1)> parameters, out bool inverted)
    {
        inverted = false;
        if (expression is NotExpression not)
        {
            inverted = true;
            expression = not.Left;
        }

        if (expression is OrExpression or) return TryParseIn(or.Left, type, parameters, out _) && TryParseIn(or.Right, type, parameters, out _);
        if (expression.GetType() != type) return false;

        T first = default;
        T1 second = default;
        if (typeof(T) == typeof(string) && expression is IWithStringParameter withString) first = (T)(object)withString.Parameter;
        else if (typeof(T) == typeof(DateTime) && expression is IWithDateParameter withDate) first = (T)(object)withDate.Parameter;
        else if (typeof(T) == typeof(int) && expression is IWithNumberParameter withInt) first = (T)(object)Convert.ToInt32(withInt.Parameter);
        else if (typeof(T) == typeof(double) && expression is IWithNumberParameter withNumber) first = (T)(object)withNumber.Parameter;
        else if (typeof(T) == typeof(TimeSpan) && expression is IWithTimeSpanParameter withTimeSpan) first = (T)(object)withTimeSpan.Parameter;
        if (typeof(T1) == typeof(string) && expression is IWithSecondStringParameter withSecondString) second = (T1)(object)withSecondString.SecondParameter;
        if (!EqualityComparer<T>.Default.Equals(first, default) && !EqualityComparer<T1>.Default.Equals(second, default)) parameters.Add((first, second));

        return true;

    }

    private static bool TryParseComparator(FilterExpression expression, Type type, out object parameter, out GroupFilterOperator gfOperator)
    {
        // comparators share a similar format:
        // Expression(selector, selector) or Expression(selector, parameter)
        gfOperator = 0;
        parameter = null;
        switch (expression)
        {
            case DateGreaterThanExpression dateGreater when dateGreater.Left?.GetType() != type:
                return false;
            case DateGreaterThanExpression dateGreater:
                gfOperator = GroupFilterOperator.GreaterThan;
                parameter = dateGreater.Parameter;
                return true;
            case DateGreaterThanEqualsExpression dateGreaterEquals when dateGreaterEquals.Left?.GetType() != type:
                return false;
            case DateGreaterThanEqualsExpression dateGreaterEquals:
                {
                    if (dateGreaterEquals.Right is not DateDiffFunction f) return false;
                    if (f.Left is not DateAddFunction add) return false;
                    if (add.Left is not TodayFunction) return false;
                    if (add.Parameter != TimeSpan.FromDays(1) - TimeSpan.FromMilliseconds(1)) return false;
                    gfOperator = GroupFilterOperator.LastXDays;
                    parameter = f.Parameter.TotalDays;
                    return true;
                }
            case NumberGreaterThanExpression numberGreater when numberGreater.Left?.GetType() != type:
                return false;
            case NumberGreaterThanExpression numberGreater:
                gfOperator = GroupFilterOperator.GreaterThan;
                parameter = numberGreater.Parameter;
                return true;
            case DateLessThanExpression dateLess when dateLess.Left?.GetType() != type:
                return false;
            case DateLessThanExpression dateLess:
                gfOperator = GroupFilterOperator.LessThan;
                parameter = dateLess.Parameter;
                return true;
            case NumberLessThanExpression numberLess when numberLess.Left?.GetType() != type:
                return false;
            case NumberLessThanExpression numberLess:
                gfOperator = GroupFilterOperator.LessThan;
                parameter = numberLess.Parameter;
                return true;
            default:
                return false;
        }
    }

    public static string GetSortingCriteria(FilterPreset filterPreset)
    {
        return string.Join("|", GetSortingCriteriaList(filterPreset).Select(a => (int)a.SortType + ";" + (int)a.SortDirection));
    }

    public static List<GroupFilterSortingCriteria> GetSortingCriteriaList(FilterPreset filter)
    {
        var results = new List<GroupFilterSortingCriteria>();
        var expression = filter.SortingExpression;
        if (expression == null)
        {
            results.Add(new GroupFilterSortingCriteria
            {
                GroupFilterID = filter.FilterPresetID,
                SortType = GroupFilterSorting.GroupName
            });
            return results;
        }

        var current = expression;
        while (current != null)
        {
            var type = current.GetType();
            GroupFilterSorting sortType = 0;
            if (type == typeof(AddedDateSortingSelector))
                sortType = GroupFilterSorting.SeriesAddedDate;
            else if (type == typeof(LastAddedDateSortingSelector))
                sortType = GroupFilterSorting.EpisodeAddedDate;
            else if (type == typeof(LastAirDateSortingSelector))
                sortType = GroupFilterSorting.EpisodeAirDate;
            else if (type == typeof(LastWatchedDateSortingSelector))
                sortType = GroupFilterSorting.EpisodeWatchedDate;
            else if (type == typeof(NameSortingSelector))
                sortType = GroupFilterSorting.GroupName;
            else if (type == typeof(AirDateSortingSelector))
                sortType = GroupFilterSorting.Year;
            else if (type == typeof(SeriesCountSortingSelector))
                sortType = GroupFilterSorting.SeriesCount;
            else if (type == typeof(UnwatchedCountSortingSelector))
                sortType = GroupFilterSorting.UnwatchedEpisodeCount;
            else if (type == typeof(MissingEpisodeCountSortingSelector))
                sortType = GroupFilterSorting.MissingEpisodeCount;
            else if (type == typeof(HighestUserRatingSortingSelector))
                sortType = GroupFilterSorting.UserRating;
            else if (type == typeof(HighestAniDBRatingSortingSelector))
                sortType = GroupFilterSorting.AniDBRating;
            else if (type == typeof(SortingNameSortingSelector))
                sortType = GroupFilterSorting.SortName;

            if (sortType != 0)
            {
                results.Add(new GroupFilterSortingCriteria
                {
                    GroupFilterID = filter.FilterPresetID,
                    SortType = sortType,
                    SortDirection = current.Descending ? GroupFilterSortDirection.Desc : GroupFilterSortDirection.Asc
                });
            }
            current = current.Next;
        }

        return results;
    }

    public static FilterExpression<bool> GetExpression(List<GroupFilterCondition> conditions, GroupFilterBaseCondition baseCondition, bool suppressErrors = false)
    {
        // forward compatibility is easier. Just map the old conditions to an expression
        if (conditions == null || conditions.Count < 1) return null;
        var first = conditions.Select((a, index) => new {Expression= GetExpression(a, suppressErrors), Index=index}).FirstOrDefault(a => a.Expression != null);
        if (first == null) return null;
        var condition = conditions.Count == 1 ? first.Expression : conditions.Skip(first.Index + 1).Aggregate(first.Expression, (a, b) =>
        {
            var result = GetExpression(b, suppressErrors);
            return result == null ? a : new AndExpression(a, result);
        });
        return baseCondition == GroupFilterBaseCondition.Exclude ? new NotExpression(condition) : condition;
    }

    private static FilterExpression<bool> GetExpression(GroupFilterCondition condition, bool suppressErrors = false)
    {
        var op = (GroupFilterOperator)condition.ConditionOperator;
        var parameter = condition.ConditionParameter;
        switch ((GroupFilterConditionType)condition.ConditionType)
        {
            case GroupFilterConditionType.CompletedSeries:
                if (op == GroupFilterOperator.Include)
                    return LegacyMappings.GetCompletedExpression();
                return new NotExpression(LegacyMappings.GetCompletedExpression());
            case GroupFilterConditionType.FinishedAiring:
                if (op == GroupFilterOperator.Include)
                    return new IsFinishedExpression();
                return new NotExpression(new IsFinishedExpression());
            case GroupFilterConditionType.MissingEpisodes:
                if (op == GroupFilterOperator.Include)
                    return new HasMissingEpisodesExpression();
                return new NotExpression(new HasMissingEpisodesExpression());
            case GroupFilterConditionType.MissingEpisodesCollecting:
                if (op == GroupFilterOperator.Include)
                    return new HasMissingEpisodesCollectingExpression();
                return new NotExpression(new HasMissingEpisodesCollectingExpression());
            case GroupFilterConditionType.HasUnwatchedEpisodes:
                if (op == GroupFilterOperator.Include)
                    return new HasUnwatchedEpisodesExpression();
                return new NotExpression(new HasUnwatchedEpisodesExpression());
            case GroupFilterConditionType.HasWatchedEpisodes:
                if (op == GroupFilterOperator.Include)
                    return new HasWatchedEpisodesExpression();
                return new NotExpression(new HasWatchedEpisodesExpression());
            case GroupFilterConditionType.UserVoted:
                if (op == GroupFilterOperator.Include)
                    return new HasPermanentUserVotesExpression();
                return new NotExpression(new HasPermanentUserVotesExpression());
            case GroupFilterConditionType.UserVotedAny:
                if (op == GroupFilterOperator.Include)
                    return new HasUserVotesExpression();
                return new NotExpression(new HasUserVotesExpression());
            case GroupFilterConditionType.Favourite:
                if (op == GroupFilterOperator.Include)
                    return new IsFavoriteExpression();
                return new NotExpression(new IsFavoriteExpression());
            case GroupFilterConditionType.AssignedTvDBInfo:
                if (op == GroupFilterOperator.Include)
                    return new HasTvDBLinkExpression();
                return new NotExpression(new HasTvDBLinkExpression());
            case GroupFilterConditionType.AssignedMovieDBInfo:
                if (op == GroupFilterOperator.Include)
                    return new HasTMDbLinkExpression();
                return new NotExpression(new HasTMDbLinkExpression());
            case GroupFilterConditionType.AssignedTvDBOrMovieDBInfo:
                if (op == GroupFilterOperator.Include)
                    return new OrExpression(new HasTvDBLinkExpression(), new HasTMDbLinkExpression());
                return new NotExpression(new OrExpression(new HasTvDBLinkExpression(), new HasTMDbLinkExpression()));
            case GroupFilterConditionType.AssignedTraktInfo:
                if (op == GroupFilterOperator.Include)
                    return new HasTraktLinkExpression();
                return new NotExpression(new HasTraktLinkExpression());
            case GroupFilterConditionType.Tag:
                return LegacyMappings.GetTagExpression(op, parameter, suppressErrors);
            case GroupFilterConditionType.CustomTags:
                return LegacyMappings.GetCustomTagExpression(op, parameter, suppressErrors);
            case GroupFilterConditionType.AirDate:
                return LegacyMappings.GetAirDateExpression(op, parameter, suppressErrors);
            case GroupFilterConditionType.LatestEpisodeAirDate:
                return LegacyMappings.GetLastAirDateExpression(op, parameter, suppressErrors);
            case GroupFilterConditionType.SeriesCreatedDate:
                return LegacyMappings.GetAddedDateExpression(op, parameter, suppressErrors);
            case GroupFilterConditionType.EpisodeAddedDate:
                return LegacyMappings.GetLastAddedDateExpression(op, parameter, suppressErrors);
            case GroupFilterConditionType.EpisodeWatchedDate:
                return LegacyMappings.GetWatchedDateExpression(op, parameter, suppressErrors);
            case GroupFilterConditionType.AnimeType:
                return LegacyMappings.GetAnimeTypeExpression(op, parameter, suppressErrors);
            case GroupFilterConditionType.VideoQuality:
                return LegacyMappings.GetVideoQualityExpression(op, parameter, suppressErrors);
            case GroupFilterConditionType.AudioLanguage:
                return LegacyMappings.GetAudioLanguageExpression(op, parameter, suppressErrors);
            case GroupFilterConditionType.SubtitleLanguage:
                return LegacyMappings.GetSubtitleLanguageExpression(op, parameter, suppressErrors);
            case GroupFilterConditionType.AnimeGroup:
                return LegacyMappings.GetGroupExpression(op, parameter, suppressErrors);
            case GroupFilterConditionType.AniDBRating:
                return LegacyMappings.GetRatingExpression(op, parameter, suppressErrors);
            case GroupFilterConditionType.UserRating:
                return LegacyMappings.GetUserRatingExpression(op, parameter, suppressErrors);
            case GroupFilterConditionType.AssignedMALInfo:
                return suppressErrors ? null : throw new NotSupportedException("MAL is Deprecated");
            case GroupFilterConditionType.EpisodeCount:
                return LegacyMappings.GetEpisodeCountExpression(op, parameter, suppressErrors);
            case GroupFilterConditionType.Year:
                return LegacyMappings.GetYearExpression(op, parameter, suppressErrors);
            case GroupFilterConditionType.Season:
                return LegacyMappings.GetSeasonExpression(op, parameter, suppressErrors);
            default:
                return suppressErrors ? null : throw new ArgumentOutOfRangeException(nameof(condition), $@"ConditionType {(GroupFilterConditionType)condition.ConditionType} is not valid");
        }
    }

    public static SortingExpression GetSortingExpression(List<GroupFilterSortingCriteria> sorting)
    {
        SortingExpression expression = null;
        SortingExpression result = null;
        foreach (var criteria in sorting)
        {
            SortingExpression expr = criteria.SortType switch
            {
                GroupFilterSorting.SeriesAddedDate => new AddedDateSortingSelector
                {
                    Descending = criteria.SortDirection == GroupFilterSortDirection.Desc
                },
                GroupFilterSorting.EpisodeAddedDate => new LastAddedDateSortingSelector
                {
                    Descending = criteria.SortDirection == GroupFilterSortDirection.Desc
                },
                GroupFilterSorting.EpisodeAirDate => new LastAirDateSortingSelector
                {
                    Descending = criteria.SortDirection == GroupFilterSortDirection.Desc
                },
                GroupFilterSorting.EpisodeWatchedDate => new LastWatchedDateSortingSelector
                {
                    Descending = criteria.SortDirection == GroupFilterSortDirection.Desc
                },
                GroupFilterSorting.GroupName => new NameSortingSelector
                {
                    Descending = criteria.SortDirection == GroupFilterSortDirection.Desc
                },
                GroupFilterSorting.Year => new AirDateSortingSelector
                {
                    Descending = criteria.SortDirection == GroupFilterSortDirection.Desc
                },
                GroupFilterSorting.SeriesCount => new SeriesCountSortingSelector
                {
                    Descending = criteria.SortDirection == GroupFilterSortDirection.Desc
                },
                GroupFilterSorting.UnwatchedEpisodeCount => new UnwatchedCountSortingSelector
                {
                    Descending = criteria.SortDirection == GroupFilterSortDirection.Desc
                },
                GroupFilterSorting.MissingEpisodeCount => new MissingEpisodeCountSortingSelector
                {
                    Descending = criteria.SortDirection == GroupFilterSortDirection.Desc
                },
                GroupFilterSorting.UserRating => new HighestUserRatingSortingSelector
                {
                    Descending = criteria.SortDirection == GroupFilterSortDirection.Desc
                },
                GroupFilterSorting.AniDBRating => new HighestAniDBRatingSortingSelector
                {
                    Descending = criteria.SortDirection == GroupFilterSortDirection.Desc
                },
                GroupFilterSorting.SortName => new SortingNameSortingSelector
                {
                    Descending = criteria.SortDirection == GroupFilterSortDirection.Desc
                },
                GroupFilterSorting.GroupFilterName => new NameSortingSelector
                {
                    Descending = criteria.SortDirection == GroupFilterSortDirection.Desc
                },
                _ => new NameSortingSelector
                {
                    Descending = criteria.SortDirection == GroupFilterSortDirection.Desc
                }
            };
            if (expression == null) result = expression = expr;
            else expression.Next = expr;
            expression = expression.Next;
        }

        return result ?? new NameSortingSelector();
    }

    public static SortingExpression GetSortingExpression(string sorting)
    {
        if (string.IsNullOrEmpty(sorting)) return new NameSortingSelector();
        var sortCriteriaList = new List<GroupFilterSortingCriteria>();
        var scrit = sorting.Split('|');
        foreach (var sortpair in scrit)
        {
            var spair = sortpair.Split(';');
            if (spair.Length != 2)
            {
                continue;
            }

            int.TryParse(spair[0], out var stype);
            int.TryParse(spair[1], out var sdir);

            if (stype > 0 && sdir > 0)
            {
                var gfsc = new GroupFilterSortingCriteria
                {
                    GroupFilterID = 0,
                    SortType = (GroupFilterSorting)stype,
                    SortDirection = (GroupFilterSortDirection)sdir
                };
                sortCriteriaList.Add(gfsc);
            }
        }

        return GetSortingExpression(sortCriteriaList);
    }
}
