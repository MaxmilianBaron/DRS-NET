using DungeonRunners.Data;
using DungeonRunners.Engine;

namespace DungeonRunners.Combat
{
    public static class MonsterUnitStatsBuilder
    {
        public static int ComputeBaseCriticalChance(float authoredCritChance)
        {
            if (authoredCritChance <= 0f) return 0;
            long authoredFixed = (long)Mathf.RoundToInt(authoredCritChance * 256f);
            long globalScalar = (long)MonsterAttackData.Instance.MonsterCriticalChance;
            return (int)((authoredFixed * globalScalar) >> 16);
        }
    }
}
