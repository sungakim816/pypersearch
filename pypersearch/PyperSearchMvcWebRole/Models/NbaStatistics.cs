using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace PyperSearchMvcWebRole.Models
{
    /// <summary>
    /// Class for Nba Player Personal Info
    /// </summary>
    public class PersonalInfo
    {
        public string Id { get; set; }
        public string FirstName { get; set; }
        public string Team { get; set; }
        public string GamesPlayed { get; set; }
        public string Minutes { get; set; }
        public string LastName { get; set; }
    }

    /// <summary>
    /// Class for NBA player statistics
    /// </summary>
    public class Statistics
    {
        public string FgMade { get; set; }
        public string FgAttempt { get; set; }
        public string FgPercentage { get; set; }
        public string FtMade { get; set; }
        public string FtAttempt { get; set; }
        public string FtPercentage { get; set; }
        public string ReboundOffense { get; set; }
        public string ReboundDefense { get; set; }
        public string ReboundTotal { get; set; }
        public string ThreepointMade { get; set; }
        public string ThreepointAttempt { get; set; }
        public string ThreepointPercentage { get; set; }
        public string Assists { get; set; }
        public string TurnOvers { get; set; }
        public string Steals { get; set; }
        public string PersonalFouls { get; set; }
        public string PointsPerGame { get; set; }
        public string Blocks { get; set; }
    }

    /// <summary>
    /// Over all implementation for NBA Player Statistics and Records
    /// </summary>
    public class NbaStatistics
    {
        public PersonalInfo PersonalInfo { get; set; }
        public Statistics Statistics { get; set; }
        public string Photo { get; set; }
        public string Link { get; set; }
        public string Framelink { get; set; }
    }
}