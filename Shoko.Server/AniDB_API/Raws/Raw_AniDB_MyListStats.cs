﻿using AniDBAPI;
using NLog;

namespace Shoko.Server.AniDB_API.Raws
{
    public class Raw_AniDB_MyListStats
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public int Animes { get; set; }
        public int Episodes { get; set; }
        public int Files { get; set; }
        public long SizeOfFiles { get; set; }
        public int AddedAnimes { get; set; }
        public int AddedEpisodes { get; set; }
        public int AddedFiles { get; set; }
        public int AddedGroups { get; set; }
        public int LeechPct { get; set; }
        public int GloryPct { get; set; }
        public int ViewedPct { get; set; }
        public int MylistPct { get; set; }
        public int ViewedMylistPct { get; set; }
        public int EpisodesViewed { get; set; }
        public int Votes { get; set; }
        public int Reviews { get; set; }
        public int ViewiedLength { get; set; }


        // default constructor
        public Raw_AniDB_MyListStats()
        {
        }

        private void InitVals()
        {
            Animes = 0;
            Episodes = 0;
            Files = 0;
            SizeOfFiles = 0;
            AddedAnimes = 0;
            AddedEpisodes = 0;
            AddedFiles = 0;
            AddedGroups = 0;
            LeechPct = 0;
            GloryPct = 0;
            ViewedPct = 0;
            MylistPct = 0;
            ViewedMylistPct = 0;
            EpisodesViewed = 0;
            Votes = 0;
            Reviews = 0;
            ViewiedLength = 0;
        }

        public Raw_AniDB_MyListStats(string sRecMessage)
        {
            InitVals();

            // remove the header info
            string[] sDetails = sRecMessage.Substring(16).Split('|');

            // 222 MYLIST STATS
            // 281|3539|4025|1509124|0|0|0|0|100|100|0|3|5|170|23|0|4001

            Animes = AniDBAPILib.ProcessAniDBInt(sDetails[0]);
            Episodes = AniDBAPILib.ProcessAniDBInt(sDetails[1]);
            Files = AniDBAPILib.ProcessAniDBInt(sDetails[2]);
            SizeOfFiles = AniDBAPILib.ProcessAniDBLong(sDetails[3]);
            AddedAnimes = AniDBAPILib.ProcessAniDBInt(sDetails[4]);
            AddedEpisodes = AniDBAPILib.ProcessAniDBInt(sDetails[5]);
            AddedFiles = AniDBAPILib.ProcessAniDBInt(sDetails[6]);
            AddedGroups = AniDBAPILib.ProcessAniDBInt(sDetails[7]);
            LeechPct = AniDBAPILib.ProcessAniDBInt(sDetails[8]);
            GloryPct = AniDBAPILib.ProcessAniDBInt(sDetails[9]);
            ViewedPct = AniDBAPILib.ProcessAniDBInt(sDetails[10]);
            MylistPct = AniDBAPILib.ProcessAniDBInt(sDetails[11]);
            ViewedMylistPct = AniDBAPILib.ProcessAniDBInt(sDetails[12]);
            EpisodesViewed = AniDBAPILib.ProcessAniDBInt(sDetails[13]);
            Votes = AniDBAPILib.ProcessAniDBInt(sDetails[14]);
            Reviews = AniDBAPILib.ProcessAniDBInt(sDetails[15]);
            ViewiedLength = AniDBAPILib.ProcessAniDBInt(sDetails[16]);
        }

        public override string ToString()
        {
            return string.Format("Raw_AniDB_MyListStats:: Animes: {0} | Files: {1}", Animes, Files);
        }
    }
}