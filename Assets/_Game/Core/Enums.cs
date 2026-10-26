namespace FES.Core
{
    /// <summary>World hierarchy level. Lower value = larger scope.</summary>
    public enum WorldLevel : byte
    {
        Continent = 0,
        Nation = 1,
        Region = 2,
        City = 3,
        Village = 4,
    }

    public static class WorldLevels
    {
        public const int Count = 5;
    }

    public enum QualityGrade : byte
    {
        Low = 0,
        Mid = 1,
        High = 2,
        Best = 3,
    }

    public static class QualityGrades
    {
        public const int Count = 4;
    }

    public enum IndustryTier : byte
    {
        Primary = 0,
        Secondary = 1,
        Tertiary = 2,
    }

    public static class IndustryTiers
    {
        public const int Count = 3;
    }

    /// <summary>Whether an entity is driven by the player or by its automation.</summary>
    public enum ControlMode : byte
    {
        Manual = 0,
        Automated = 1,
    }

    /// <summary>Simulation level of detail: per-entity stepping or summarized node-level flows.</summary>
    public enum SimDetail : byte
    {
        Aggregate = 0,
        Detailed = 1,
    }
}
