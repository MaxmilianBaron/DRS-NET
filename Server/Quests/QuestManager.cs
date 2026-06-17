using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DungeonRunners.Engine;
using DungeonRunners.Utilities;
using DungeonRunners.Networking;
using DungeonRunners.Data;

namespace DungeonRunners.Gameplay
{
    public class QuestManager
    {
        private static QuestManager _instance;
        public static QuestManager Instance => _instance ??= new QuestManager();

        private Dictionary<string, PlayerQuestState> _playerQuests = new Dictionary<string, PlayerQuestState>();
        private Action<RRConnection, byte, byte, byte[]> _sendPacket;
        private Func<RRConnection, LEWriter, bool> _writeEntitySynch;
        private static readonly object _killGCByLabelLock = new object();
        private static Dictionary<string, HashSet<string>> _packageBackedKillGCByLabel;
        private static readonly Regex _lineCommentRegex = new Regex(@"//.*?$", RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex _killObjectiveBlockRegex = new Regex(@"extends\s+quests\.base\.KillObjective\s*\{(?<body>.*?)\}", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex _questLabelRegex = new Regex(@"\bLabel\s*=\s*""(?<label>[^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _questMonsterTypeRegex = new Regex(@"\bMonsterType\d*\s*=\s*(?<monster>[^;\s]+)\s*;", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Dictionary<string, HashSet<string>> _killGCByLabel =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            { "''Laid-off'' Mutant Fizzmaster Guards", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon01.mob.lowerOneOff.raceA.base2", "world.dungeon01.mob.lowerOneOff.raceA.base_champ2", "world.dungeon01.mob.lowerOneOff.raceA.base_hero2" } },
            { "''Laid-off'' Mutant Fizzmaster Mixers", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon01.mob.lowerOneOff.raceA.base3", "world.dungeon01.mob.lowerOneOff.raceA.base_champ3" } },
            { "''Spar'' with 25 Partners", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon16.mob.lower.raceA.base1", "world.dungeon16.mob.lower.raceA.base2", "world.dungeon16.mob.lower.raceA.base_champ1", "world.dungeon16.mob.lower.raceA.base_champ2", "world.dungeon16.mob.lower.raceA.base_hero1", "world.dungeon16.mob.lower.raceA.base_hero2", "world.dungeon16.mob.lower.raceB.base1", "world.dungeon16.mob.lower.raceB.base2" } },
            { "''Speak'' with Orok Scouts", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon01.mob.upperOneOff.raceA.base2" } },
            { "''Speak'' with Orok Spawns", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon01.mob.upperOneOff.raceA.base1" } },
            { "''Speak'' with Orok Warriors", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon01.mob.upperOneOff.raceA.base3" } },
            { "Bounced Blademasters", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon01.mob.lower.raceA.base3" } },
            { "Bounced Broodlings", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon01.mob.lower.raceA.base1" } },
            { "Candy Fabricators", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon08.data.Q03_CandyGenerators_L03_off1a.Q03_CandyGenerator_L03_off1a_1", "world.dungeon08.data.Q03_CandyGenerators_L03_off1a.Q03_CandyGenerator_L03_off1a_2", "world.dungeon08.data.Q03_CandyGenerators_L03_off1a.Q03_CandyGenerator_L03_off1a_3", "world.dungeon08.data.Q03_CandyGenerators_L03_off1a.Q03_CandyGenerator_L03_off1a_4", "world.dungeon08.data.Q03_CandyGenerators_L03_off1a.Q03_CandyGenerator_L03_off1a_5", "world.dungeon08.data.Q03_CandyGenerators_L04_off1a.Q03_CandyGenerator_L04_off1a_1", "world.dungeon08.data.Q03_CandyGenerators_L04_off1a.Q03_CandyGenerator_L04_off1a_2", "world.dungeon08.data.Q03_CandyGenerators_L04_off1a.Q03_CandyGenerator_L04_off1a_3", "world.dungeon08.data.Q03_CandyGenerators_L04_off1a.Q03_CandyGenerator_L04_off1a_4", "world.dungeon08.data.Q03_CandyGenerators_L04_off1a.Q03_CandyGenerator_L04_off1a_5", "world.dungeon08.data.Q03_CandyGenerators_L05_off1a.Q03_CandyGenerator_L05_off1a_1", "world.dungeon08.data.Q03_CandyGenerators_L05_off1a.Q03_CandyGenerator_L05_off1a_2", "world.dungeon08.data.Q03_CandyGenerators_L05_off1a.Q03_CandyGenerator_L05_off1a_3", "world.dungeon08.data.Q03_CandyGenerators_L05_off1a.Q03_CandyGenerator_L05_off1a_4", "world.dungeon08.data.Q03_CandyGenerators_L05_off1a.Q03_CandyGenerator_L05_off1a_5", "world.dungeon08.data.Q03_CandyGenerators_L07_off1a.Q03_CandyGenerator_L07_off1a_1", "world.dungeon08.data.Q03_CandyGenerators_L07_off1a.Q03_CandyGenerator_L07_off1a_2", "world.dungeon08.data.Q03_CandyGenerators_L07_off1a.Q03_CandyGenerator_L07_off1a_3", "world.dungeon08.data.Q03_CandyGenerators_L07_off1a.Q03_CandyGenerator_L07_off1a_4", "world.dungeon08.data.Q03_CandyGenerators_L07_off1a.Q03_CandyGenerator_L07_off1a_5", "world.dungeon08.data.Q03_CandyGenerators_L07_off2a.Q03_CandyGenerator_L07_off2a_1", "world.dungeon08.data.Q03_CandyGenerators_L07_off2a.Q03_CandyGenerator_L07_off2a_2", "world.dungeon08.data.Q03_CandyGenerators_L07_off2a.Q03_CandyGenerator_L07_off2a_3", "world.dungeon08.data.Q03_CandyGenerators_L07_off2a.Q03_CandyGenerator_L07_off2a_4", "world.dungeon08.data.Q03_CandyGenerators_L07_off2a.Q03_CandyGenerator_L07_off2a_5" } },
            { "Clobber Titanic Tom", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon16.mob.Quest.Q16_c1_01" } },
            { "Countess Crypt Mistress", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.Dungeon11.mob.master05_1a" } },
            { "Crushious the Head Banging Crusher", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.Dungeon11.mob.master07_2a" } },
            { "Defeat NaijeerianConsular419", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon16.mob.Quest.Q16_d1_01" } },
            { "Defeat Ponderous Luchadores with Liger", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.Dungeon16.mob.Quest.Q08_a1_01" } },
            { "Destroy Candy Fabricators", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon08.data.Q03_CandyGenerators_L01_off1a.Q03_CandyGenerator_L01_off1a_1", "world.dungeon08.data.Q03_CandyGenerators_L01_off1a.Q03_CandyGenerator_L01_off1a_2", "world.dungeon08.data.Q03_CandyGenerators_L01_off1a.Q03_CandyGenerator_L01_off1a_3", "world.dungeon08.data.Q03_CandyGenerators_L01_off1a.Q03_CandyGenerator_L01_off1a_4", "world.dungeon08.data.Q03_CandyGenerators_L01_off1a.Q03_CandyGenerator_L01_off1a_5" } },
            { "Destroy Pinata Turrets", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q07_pinata_turret_c1" } },
            { "Destroy Pinatas", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.q07_pinata_a1", "world.dungeon15.mob.quest.q07_pinata_b1", "world.dungeon15.mob.quest.q07_pinata_b2", "world.dungeon15.mob.quest.q07_pinata_b3", "world.dungeon15.mob.quest.q07_pinata_b4", "world.dungeon15.mob.quest.q07_pinata_c1" } },
            { "Destroy the Pinata", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.q07_pinata_a1", "world.dungeon15.mob.quest.q07_pinata_c1" } },
            { "Destroy the Pinata Antiguo del Fantasma on Level 2", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q07_l02_pinata_hero" } },
            { "Destroy the Pinata Antiguo del Fantasma on Level 4", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q07_l04_pinata_hero" } },
            { "Destroy the Pinata Antiguo del Fantasma on Level 6", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q07_l06_pinata_hero" } },
            { "Destroy the Three Evil Heroes on Level 2", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q07_d_l02_gladiator_1_hero", "world.dungeon15.mob.quest.Q07_d_l02_gladiator_2_hero", "world.dungeon15.mob.quest.Q07_d_l02_gladiator_3_hero" } },
            { "Destroy the Three Evil Heroes on Level 4", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q07_d_l04_gladiator_1_hero", "world.dungeon15.mob.quest.Q07_d_l04_gladiator_2_hero", "world.dungeon15.mob.quest.Q07_d_l04_gladiator_3_hero" } },
            { "Destroy the Three Evil Heroes on Level 6", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q07_d_l06_gladiator_1_hero", "world.dungeon15.mob.quest.Q07_d_l06_gladiator_2_hero", "world.dungeon15.mob.quest.Q07_d_l06_gladiator_3_hero" } },
            { "Destroyed Barrels O' Fizz", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon03.mob.quest_level05_off1b" } },
            { "Disassemble FRaNK", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon01.mob.master07_2a" } },
            { "Eliminate Rattle Ear", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon01.mob.Quest.Q01_a4_level06" } },
            { "Eliminate Rattle Elbow", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon01.mob.Quest.D00_Q02_a5_level04_off1_02" } },
            { "Eliminate Rattle Eye", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon01.mob.Quest.Q01_a4_level02" } },
            { "Eliminate Rattle Neck", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon01.mob.Quest.Q01_a4_level04" } },
            { "Eliminate Rattle Tail", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon01.mob.Quest.D00_Q02_a5_level04_off1_01" } },
            { "Eliminate Rattle Toe", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon01.mob.boss_guard" } },
            { "Eliminate Sissirat", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon01.mob.boss" } },
            { "Kill Abadabadooddon", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon08.mob.master03_1a" } },
            { "Kill Abaddon", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon08.mob.boss" } },
            { "Kill Abaddon's Ciphers", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon08.mob.quest.Ghost_L01_off1a_1a", "world.dungeon08.mob.quest.Ghost_L01_off1a_2a", "world.dungeon08.mob.quest.Ghost_L01_off1a_Major", "world.dungeon08.mob.quest.Ghost_L04_off1a_1a", "world.dungeon08.mob.quest.Ghost_L04_off1a_2a", "world.dungeon08.mob.quest.Ghost_L04_off1a_Major", "world.dungeon08.mob.quest.Ghost_L07_off1a_1a", "world.dungeon08.mob.quest.Ghost_L07_off1a_2a", "world.dungeon08.mob.quest.Ghost_L07_off1a_Major" } },
            { "Kill Abadumdadumdummmddon", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon08.mob.master07_1a" } },
            { "Kill Abadunkadunkaddonna", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon08.mob.master05_1a" } },
            { "Kill Abadupercalifragilisticexpialidociousddonna", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon08.mob.master07_2a" } },
            { "Kill Abazabaddon", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon08.mob.master01_1a" } },
            { "Kill Abba Labbas", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon08.mob.quest.AabbaLaabas_Caster", "world.dungeon08.mob.quest.AabbaLaabas_Caster_Candy", "world.dungeon08.mob.quest.AabbaLaabas_Caster_Candy_Summoned", "world.dungeon08.mob.quest.AabbaLaabas_Caster_Hero_Candy", "world.dungeon08.mob.quest.AabbaLaabas_Melee", "world.dungeon08.mob.quest.AabbaLaabas_Melee_Candy", "world.dungeon08.mob.quest.AabbaLaabas_Melee_Candy_Summoned", "world.dungeon08.mob.quest.AabbaLaabas_Melee_Hero_Candy" } },
            { "Kill Abracadabraddonna", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon08.mob.master04_1a" } },
            { "Kill Halfassin White", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon09.mob.quest.Q07_b1_hero1", "world.dungeon09.mob.quest.Q07_b2_hero1" } },
            { "Kill The Abominable Snow Dog", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.solo.dungeon_snowman.mob.boss2" } },
            { "Kill [GG] Your Little Sister", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon09.mob.quest.Q07_c1_hero1" } },
            { "Kill the Keeper of the Candies", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon08.mob.quest.Keeper_l05_off1a_2", "world.dungeon08.mob.quest.Keeper_l07_off2a_2" } },
            { "Kill the Queen of Shadows", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon09.mob.boss" } },
            { "Kill the Taker of the Candies", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon08.mob.quest.Keeper_l07_off2a_3" } },
            { "Kill the Watcher of the Candies", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon08.mob.quest.Keeper_l03_off1a", "world.dungeon08.mob.quest.Keeper_l05_off1a_1", "world.dungeon08.mob.quest.Keeper_l07_off2a_1" } },
            { "KrypKogulous", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.Dungeon11.mob.master03_1a" } },
            { "M3R0CK", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon06.mob.quest.Q04_3" } },
            { "Negotiate with Vergrim", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon03.mob.boss" } },
            { "Princess_Slayer57", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon06.mob.quest.Q04_4" } },
            { "Slain Famous Shadow Fade Crooners", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon02.mob.loweroneoff.racea.evo_champ1", "world.dungeon02.mob.loweroneoff.racea.evo_hero1" } },
            { "Slain Fire Mutant Hulks", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon02.mob.upper.raceA.base3", "world.dungeon02.mob.upper.raceA.depth1", "world.dungeon02.mob.upper.raceA.depth_champ1", "world.dungeon02.mob.upper.raceA.depth_hero1" } },
            { "Slain Fire Mutant Pincers", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon02.mob.upper.raceA.base1", "world.dungeon02.mob.upper.raceA.base_champ1", "world.dungeon02.mob.upper.raceA.base_hero1" } },
            { "Slain Fire Mutant Pukers", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon02.mob.upper.raceA.base2", "world.dungeon02.mob.upper.raceA.base_champ2", "world.dungeon02.mob.upper.raceA.base_hero2" } },
            { "Slain Fire Mutant Twins", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon02.mob.upper.raceA.base4", "world.dungeon02.mob.upper.raceA.evo1", "world.dungeon02.mob.upper.raceA.evo_champ1", "world.dungeon02.mob.upper.raceA.evo_hero1" } },
            { "Slain Ice Orok AC Overseers", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon05.mob.upper.racea.evo_champ1", "world.dungeon05.mob.upper.racea.evo_hero1" } },
            { "Slain Ice Orok AC Repairmen", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon05.mob.upper.racea.evo1" } },
            { "Slain Ice Orok Agitators", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon02.mob.upperoneoff.racea.base1", "world.dungeon02.mob.upperoneoff.racea.base2", "world.dungeon02.mob.upperoneoff.racea.base4", "world.dungeon02.mob.upperoneoff.racea.depth1" } },
            { "Slain Ice Orok Juggernaught Agitator Leaders", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon02.mob.upperoneoff.racea.depth_champ1", "world.dungeon02.mob.upperoneoff.racea.depth_hero1" } },
            { "Slain Ice Orok Runt Agitator Leaders", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon02.mob.upperoneoff.racea.base_champ1", "world.dungeon02.mob.upperoneoff.racea.base_hero1" } },
            { "Slain Ice Orok Scout Agitator Leaders", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon02.mob.upperoneoff.racea.base_champ2", "world.dungeon02.mob.upperoneoff.racea.base_hero2" } },
            { "Slain Ice Orok Warrior Agitator Leaders", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon02.mob.upperoneoff.racea.evo_champ1", "world.dungeon02.mob.upperoneoff.racea.evo_hero1" } },
            { "Slain Ice Orok Warrior Agitators", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon02.mob.upperoneoff.racea.evo1" } },
            { "Slain Particularly Rude Broodling", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon01.mob.Quest.Q12_a3_Level01" } },
            { "Slain Poison Mutant Twin Leaders", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon05.mob.upper.raceb.evo_champ1", "world.dungeon05.mob.upper.raceb.evo_hero1" } },
            { "Slain Poison Mutant Twins", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon05.mob.upper.raceb.evo1" } },
            { "Slain Rattle Family Friend", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon03.mob.boss_guard1", "world.dungeon03.mob.boss_guard2" } },
            { "Slain Shadow Fade Crooners", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon02.mob.loweroneoff.racea.evo1" } },
            { "Slain Shadow Fade Factory Foremen", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon02.mob.loweroneoff.racea.base_champ1", "world.dungeon02.mob.loweroneoff.racea.base_hero1" } },
            { "Slain Shadow Fade Factory Worker", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon02.mob.loweroneoff.racea.base1" } },
            { "Slain Whiskers Blademasters", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon01.mob.lower.raceA.base3", "world.dungeon01.mob.lower.raceA.base_champ3", "world.dungeon01.mob.lower.raceA.base_hero3", "world.dungeon01.mob.upper.raceA.base3", "world.dungeon01.mob.upper.raceA.base_champ3", "world.dungeon01.mob.upper.raceA.base_hero3" } },
            { "Slain Whiskers Broodlings", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon01.mob.lower.raceA.base1", "world.dungeon01.mob.lower.raceA.base_champ1", "world.dungeon01.mob.lower.raceA.base_hero1", "world.dungeon01.mob.upper.raceA.base2", "world.dungeon01.mob.upper.raceA.base5", "world.dungeon01.mob.upper.raceA.base6", "world.dungeon01.mob.upper.raceA.base7", "world.dungeon01.mob.upper.raceA.base_champ2", "world.dungeon01.mob.upper.raceA.base_champ5", "world.dungeon01.mob.upper.raceA.base_champ6", "world.dungeon01.mob.upper.raceA.base_champ7", "world.dungeon01.mob.upper.raceA.base_hero2" } },
            { "Slay Algor", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon02.mob.boss" } },
            { "Slay Champion Gladiators", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q07_a_l02_gladiator_1_champion", "world.dungeon15.mob.quest.Q07_a_l02_gladiator_2_champion", "world.dungeon15.mob.quest.Q07_a_l02_gladiator_3_champion", "world.dungeon15.mob.quest.Q07_b_l04_gladiator_1_champion", "world.dungeon15.mob.quest.Q07_b_l04_gladiator_2_champion", "world.dungeon15.mob.quest.Q07_b_l04_gladiator_3_champion" } },
            { "Slay Dew Valley Pups", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon00.mob.melee01.rank1", "world.dungeon00.mob.melee01.rank2", "world.dungeon00.mob.melee01.rank3" } },
            { "Slay Dew Valley Wolves", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon00.mob.melee02.rank1", "world.dungeon00.mob.melee02.rank2", "world.dungeon00.mob.melee02.rank3" } },
            { "Slay Fade Footsoldiers", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "creatures.fade.footsoldier.Poison.Grunt" } },
            { "Slay Grunt Gladiators", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q07_a_l02_gladiator_1_grunt", "world.dungeon15.mob.quest.Q07_a_l02_gladiator_2_grunt", "world.dungeon15.mob.quest.Q07_a_l02_gladiator_3_grunt", "world.dungeon15.mob.quest.Q07_b_l04_gladiator_1_grunt", "world.dungeon15.mob.quest.Q07_b_l04_gladiator_2_grunt", "world.dungeon15.mob.quest.Q07_b_l04_gladiator_3_grunt" } },
            { "Slay Hero Gladiators", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q07_a_l02_gladiator_1_Hero", "world.dungeon15.mob.quest.Q07_a_l02_gladiator_2_Hero", "world.dungeon15.mob.quest.Q07_a_l02_gladiator_3_Hero", "world.dungeon15.mob.quest.Q07_b_l04_gladiator_1_hero", "world.dungeon15.mob.quest.Q07_b_l04_gladiator_2_hero", "world.dungeon15.mob.quest.Q07_b_l04_gladiator_3_hero" } },
            { "Slay Mutant Hulk", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "creatures.mutants.hulk.Poison.Grunt" } },
            { "Slay Pillagers", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q03_l04_1a", "world.dungeon15.mob.quest.Q03_l04_1b", "world.dungeon15.mob.quest.Q03_l04_1c" } },
            { "Slay Punkins", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q02_3" } },
            { "Slay Rattle Tooth", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon00.mob.boss" } },
            { "Slay Rattle Tooth's Vengeful Blademasters", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q04_1a" } },
            { "Slay Rattle Tooth's Vengeful Blasters", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q04_1b" } },
            { "Slay Rattle Tooth's Vengeful Soul", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.master01_1a" } },
            { "Slay Rotgut", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon06.mob.boss" } },
            { "Slay Sissirat's Ghost", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.master04_1a_ghost" } },
            { "Slay Snow Monster's Best Friend", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.solo.dungeon_snowman.mob.boss" } },
            { "Slay Tomblifters", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q03_l06_1a", "world.dungeon15.mob.quest.Q03_l06_1b", "world.dungeon15.mob.quest.Q03_l06_1c" } },
            { "Slay Vergrim's Ghost", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.master07_1a_ghost" } },
            { "Slay Whisker Blademasters", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon00.mob.melee04.rank1", "world.dungeon00.mob.melee04.rank2", "world.dungeon00.mob.melee04.rank3", "world.dungeon01.mob.upper.raceA.base3" } },
            { "Slay Whisker Broodlings", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "creatures.whiskers.broodling.Poison.Grunt" } },
            { "Slay Whisker Ratlings", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon00.mob.melee03.rank1", "world.dungeon00.mob.melee03.rank2", "world.dungeon00.mob.melee03.rank3" } },
            { "Slay Whisker Venomspitters", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon01.mob.lower.raceA.base2" } },
            { "Slay Wolf Pups", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.solo.dungeon_snowman.mob.racea.base1a", "world.solo.dungeon_snowman.mob.racea.base1b" } },
            { "Slay a Hero Gladiator", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q07_b_l04_gladiator_1_hero", "world.dungeon15.mob.quest.Q07_b_l04_gladiator_2_hero", "world.dungeon15.mob.quest.Q07_b_l04_gladiator_3_hero" } },
            { "Slay the Banned Farmer on Level 1", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q02_l01_4a" } },
            { "Slay the Banned Farmer on Level 2", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q02_l02_4a" } },
            { "Slay the Banned Farmer on Level 3", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q02_l03_4a" } },
            { "Slay the Banned Farmer on Level 4", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q02_l04_4a" } },
            { "Slay the Banned Farmer on the Level 1 Branch", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q02_L01_off1_4a", "world.dungeon15.mob.quest.Q02_l01_off1_4a" } },
            { "Slay the Banned Farmer on the Level 3 Branch", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q02_L03_off1_4a", "world.dungeon15.mob.quest.Q02_l03_off1_4a" } },
            { "Slay the Banned Farmer on the Level 4 Branch", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q02_L04_off1_4a", "world.dungeon15.mob.quest.Q02_l04_off1_4a" } },
            { "Slay the Banned Uber Farmer on Level 5", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q02_l05_4b" } },
            { "Slay the Banned Uber Farmer on Level 5 branch", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q02_l05_off1_4b" } },
            { "Slay the Banned Uber Farmer on Level 6", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q02_l06_4b" } },
            { "Slay the Banned Uber Farmer on Level 7", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q02_l07_4b" } },
            { "Slay the Fizzmaster", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon03.mob.quest_level05_off1a" } },
            { "Slay the Uber Banned Farmer in the Level 7 branch", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q02_l07_off1_4b" } },
            { "Slay the Uber Banned Farmer on Level 5", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q02_l05_4b" } },
            { "Slay the Uber Banned Farmer on Level 5 branch", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q02_l05_off1_4b" } },
            { "Slay the Uber Banned Farmer on Level 6", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q02_l06_4b" } },
            { "Slay the Uber Banned Farmer on Level 7", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon15.mob.quest.Q02_l07_4b" } },
            { "Spar with 5 Local Tough Guys", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon16.mob.upper.raceA.base_champ1", "world.dungeon16.mob.upper.raceA.base_champ2", "world.dungeon16.mob.upper.raceA.base_hero1", "world.dungeon16.mob.upper.raceA.base_hero2", "world.dungeon16.mob.upper.raceB.base_champ1", "world.dungeon16.mob.upper.raceB.base_champ2", "world.dungeon16.mob.upper.raceB.base_hero1", "world.dungeon16.mob.upper.raceB.base_hero2" } },
            { "Take Out the Junior Technician", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon09.mob.quest.Q09_a8_champ1" } },
            { "Take Out the Senior Technician", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon09.mob.quest.Q09_a8_hero1" } },
            { "Take-out Capwn", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon01.mob.master03_1a" } },
            { "Take-out FizzStar-soaked Blademaster", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon01.mob.lower.raceA.base3", "world.dungeon01.mob.lower.raceA.base_champ3", "world.dungeon01.mob.lower.raceA.base_hero3" } },
            { "Take-out Orok Scout", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon01.mob.upperOneOff.raceA.base2", "world.dungeon01.mob.upperOneOff.raceA.base_champ2", "world.dungeon01.mob.upperOneOff.raceA.base_hero2" } },
            { "Take-out Orok Spawn", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon01.mob.upperOneOff.raceA.base1", "world.dungeon01.mob.upperOneOff.raceA.base_champ1", "world.dungeon01.mob.upperOneOff.raceA.base_hero1" } },
            { "Take-out Orok Warrior", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon01.mob.upperOneOff.raceA.base3", "world.dungeon01.mob.upperOneOff.raceA.base_champ3", "world.dungeon01.mob.upperOneOff.raceA.base_hero3" } },
            { "Teach UndercoverPancakes975 a Lesson", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon16.mob.Quest.Q16_b3_01" } },
            { "Tofunk the Funky DJ", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.Dungeon11.mob.master01_1a" } },
            { "Toxic Blademasters Traitors", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon03.mob.melee12" } },
            { "Toxic Venomspitter Traitors", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon03.mob.ranged08", "world.dungeon03.mob.ranged08_champ", "world.dungeon03.mob.ranged08_hero" } },
            { "Toxic Whisker Shaman Traitors", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon03.mob.ranged07", "world.dungeon03.mob.ranged07_champ", "world.dungeon03.mob.ranged07_hero" } },
            { "Whisker Forerat removed", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world.dungeon01.mob.master04_1a" } },
        };


        private static Dictionary<string, HashSet<string>> GetKillGCByLabel()
        {
            if (_packageBackedKillGCByLabel != null)
                return _packageBackedKillGCByLabel;

            lock (_killGCByLabelLock)
            {
                if (_packageBackedKillGCByLabel == null)
                    _packageBackedKillGCByLabel = LoadKillGCByLabel();
                return _packageBackedKillGCByLabel;
            }
        }

        private static Dictionary<string, HashSet<string>> LoadKillGCByLabel()
        {
            var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var killLabelEntry in _killGCByLabel)
                result[killLabelEntry.Key] = new HashSet<string>(killLabelEntry.Value, StringComparer.OrdinalIgnoreCase);

            int docsScanned = 0;
            int authoredLabels = 0;
            string gcDir = DungeonRunners.Core.DataPaths.GcDir;
            try
            {
                if (Directory.Exists(gcDir))
                {
                    foreach (string path in Directory.EnumerateFiles(gcDir, "Q*.gc", SearchOption.AllDirectories))
                    {
                        ScanKillObjectiveText(File.ReadAllText(path), result, ref authoredLabels);
                        docsScanned++;
                    }
                }

                var packageCatalog = PackageCatalog.Instance;
                if (!packageCatalog.IsLoaded)
                    packageCatalog.LoadFromAssets();
                if (packageCatalog.IsLoaded)
                {
                    foreach (var doc in packageCatalog.EnumerateGcTextDocuments("Q*.gc"))
                    {
                        if (doc == null || string.IsNullOrWhiteSpace(doc.Text))
                            continue;
                        ScanKillObjectiveText(doc.Text, result, ref authoredLabels);
                        docsScanned++;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[QUEST-KILL-CATALOG] state=failed source=packageCatalog message='{ex.Message}'");
            }

            Debug.LogError($"[QUEST-KILL-CATALOG] source=flat+PackageCatalog labels={result.Count} authoredLabels={authoredLabels} docsScanned={docsScanned} sourceFunction=Quest::readObjectives@0x005BD560 Quest::processUpdate@0x005BD170 pkg=Q*.gc:quests.base.KillObjective");
            return result;
        }

        private static void ScanKillObjectiveText(string text, Dictionary<string, HashSet<string>> result, ref int authoredLabels)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            string stripped = _lineCommentRegex.Replace(text, "");

            foreach (Match blockMatch in _killObjectiveBlockRegex.Matches(stripped))
            {
                string body = blockMatch.Groups["body"].Value;
                var labelMatch = _questLabelRegex.Match(body);
                if (!labelMatch.Success)
                    continue;

                string label = labelMatch.Groups["label"].Value.Trim();
                if (string.IsNullOrWhiteSpace(label))
                    continue;

                if (!result.TryGetValue(label, out var monsters))
                {
                    monsters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    result[label] = monsters;
                }

                int before = monsters.Count;
                foreach (Match monsterMatch in _questMonsterTypeRegex.Matches(body))
                {
                    string monster = NormalizeQuestMonsterType(monsterMatch.Groups["monster"].Value);
                    if (!string.IsNullOrWhiteSpace(monster))
                        monsters.Add(monster);
                }
                if (monsters.Count > before)
                    authoredLabels++;
            }
        }

        private static string NormalizeQuestMonsterType(string value)
        {
            string monster = (value ?? "").Trim().Trim('"').Trim('\'').Replace('\\', '.').Replace('/', '.');
            if (monster.EndsWith(".gc", StringComparison.OrdinalIgnoreCase))
                monster = monster.Substring(0, monster.Length - 3);
            return monster.Trim();
        }

        private QuestManager()
        {
            Debug.Log("[QUEST-MANAGER] state=initialized");
        }

        public void SetSendCallback(Action<RRConnection, byte, byte, byte[]> sendCallback)
        {
            _sendPacket = sendCallback;
        }

        public void SetEntitySynchCallback(Func<RRConnection, LEWriter, bool> writeEntitySynch)
        {
            _writeEntitySynch = writeEntitySynch;
        }

        private bool WriteEntitySynchAndEnd(RRConnection conn, LEWriter writer, string packetName)
        {
            if (_writeEntitySynch == null)
            {
                Debug.LogError($"[{packetName}] state=missing target=entitySynchCallback");
                return false;
            }
            if (!_writeEntitySynch(conn, writer))
            {
                Debug.LogError($"[{packetName}] state=unavailable target=entitySynchWrite");
                return false;
            }
            writer.WriteByte(0x06);
            return true;
        }

        private static bool IsAutoAcceptOnQuery(QuestData questData)
        {
            if (questData == null || string.IsNullOrWhiteSpace(questData.id))
                return false;

            GCNode questNode = GCDatabase.Instance?.ResolveWithInheritance(questData.id);
            GCNode description = questNode?.GetChild("Description");
            return description != null && description.GetBool("AutoAcceptOnQuery");
        }

        public bool CanQueryComplete(ActiveQuest activeQuest)
        {
            if (activeQuest == null)
                return false;

            var objectives = activeQuest.Objectives;
            if (objectives != null && objectives.Count > 0)
                return objectives.All(o => o.IsComplete);

            var questData = DatabaseLoader.Quests.FirstOrDefault(q =>
                q.id.Equals(activeQuest.QuestId, StringComparison.OrdinalIgnoreCase));
            return IsAutoAcceptOnQuery(questData);
        }

        public void InitializePlayer(string connId, List<ActiveQuest> activeQuests,
                                        List<string> completedQuests, List<string> unlockedCheckpoints,
                                        int playerLevel = 1)
        {
            var state = new PlayerQuestState
            {
                ConnId = connId,
                Level = playerLevel,
                ActiveQuests = activeQuests ?? new List<ActiveQuest>(),
                CompletedQuests = completedQuests ?? new List<string>(),
                UnlockedCheckpoints = unlockedCheckpoints ?? new List<string>()
            };
            _playerQuests[connId] = state;
            Debug.Log($"[QUEST-MANAGER] conn={connId} level={playerLevel} active={state.ActiveQuests.Count} completed={state.CompletedQuests.Count}");
        }

        public PlayerQuestState GetPlayerState(string connId)
        {
            if (_playerQuests.TryGetValue(connId, out var state))
                return state;
            return null;
        }

        public void RemovePlayer(string connId)
        {
            _playerQuests.Remove(connId);
        }

        public ActiveQuest GetQuestByInstanceId(string connId, uint instanceId)
        {
            var state = GetPlayerState(connId);
            return state?.ActiveQuests.FirstOrDefault(q => q.InstanceId == instanceId);
        }

        public bool RemoveQuestByInstanceId(string connId, uint instanceId)
        {
            var state = GetPlayerState(connId);
            if (state == null) return false;
            return state.ActiveQuests.RemoveAll(q => q.InstanceId == instanceId) > 0;
        }

        public QuestAcceptResult AcceptQuest(string connId, string questId, string npcId)
        {
            Debug.LogError($"[QUEST-MANAGER] action=accept quest={questId}");
            var result = new QuestAcceptResult { Success = false };

            var playerState = GetPlayerState(connId);
            if (playerState == null) return result;

            if (playerState.ActiveQuests.Any(q => q.QuestId.Equals(questId, StringComparison.OrdinalIgnoreCase)))
                return result;

            var questData = DatabaseLoader.Quests.FirstOrDefault(q =>
                q.id.Equals(questId, StringComparison.OrdinalIgnoreCase));
            if (questData == null) return result;

            bool alreadyCompleted = playerState.CompletedQuests.Any(q =>
                q.Equals(questId, StringComparison.OrdinalIgnoreCase));
            if (alreadyCompleted && !questData.repeatable) return result;
            if (alreadyCompleted && questData.repeatable)
                playerState.CompletedQuests.RemoveAll(q => q.Equals(questId, StringComparison.OrdinalIgnoreCase));

            var activeQuest = new ActiveQuest
            {
                QuestId = questId,
                QuestGiverId = npcId,
                AcceptedAt = DateTime.UtcNow,
                Objectives = new List<QuestProgress>()
            };

            if (questData.objectives != null)
            {
                foreach (var obj in questData.objectives)
                {
                    activeQuest.Objectives.Add(new QuestProgress
                    {
                        ObjectiveName = obj.name ?? "MainObjective",
                        Type = obj.type ?? "kill",
                        Target = obj.target ?? "",
                        Label = obj.label ?? obj.target ?? "Unknown",
                        Required = obj.count > 0 ? obj.count : 1,
                        Current = 0
                    });
                }
            }

            playerState.ActiveQuests.Add(activeQuest);
            result.Success = true;
            result.Quest = activeQuest;
            result.QuestData = questData;
            return result;
        }

        public void HandleAccept(RRConnection conn, LEReader reader)
        {
            try
            {
                uint npcEntityId = reader.ReadUInt32();
                byte gcTypeIndicator = reader.ReadByte();
                uint questHash = reader.ReadUInt32();
                Debug.LogError($"[QUEST-ACCEPT] npc={npcEntityId} hash=0x{questHash:X8}");

                HandleAcceptConfirmed(conn, npcEntityId, questHash);

            }
            catch (Exception ex)
            {
                Debug.LogError($"[QUEST-ACCEPT] state=failed message='{ex.Message}'");
            }
        }

        public void HandleQuery(RRConnection conn, LEReader reader)
        {
            try
            {
                if (reader.Remaining < 9)
                {
                    Debug.LogError($"[QUEST-QUERY] state=short remaining={reader.Remaining}");
                    return;
                }

                uint npcEntityId = reader.ReadUInt32();
                byte gcTypeIndicator = reader.ReadByte();
                uint questHash = reader.ReadUInt32();

                Debug.LogError($"[QUEST-QUERY] npc={npcEntityId} hash=0x{questHash:X8}");

                if (DatabaseLoader.QuestsByHash.TryGetValue(questHash, out var quest))
                {
                    SendQueryResponse(conn, questHash, npcEntityId);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[QUEST-QUERY] state=failed message='{ex.Message}'");
            }
        }


        public void Handle0x03(RRConnection conn, LEReader reader)
        {
            uint instanceId = reader.ReadUInt32();
            Debug.LogError($"[QUEST-0x03] instanceId={instanceId} action=abandon");

            var quest = GetQuestByInstanceId(conn.ConnId.ToString(), instanceId);
            if (quest != null)
            {
                RemoveQuestByInstanceId(conn.ConnId.ToString(), instanceId);
                Debug.LogError($"[QUEST-ABANDON] quest={quest.QuestId} state=removed");
            }
            SendRemovePacket(conn, instanceId);
        }

        public void HandleComplete(RRConnection conn, LEReader reader)
        {
            try
            {
                uint instanceId = reader.ReadUInt32();
            Debug.LogError($"[QUEST-COMPLETE] instanceId={instanceId}");
                var activeQuest = GetQuestByInstanceId(conn.ConnId.ToString(), instanceId);
                if (activeQuest == null)
                {
                    Debug.LogError("[QUEST-COMPLETE] state=notFound");
                    return;
                }
                if (!activeQuest.Objectives.All(o => o.IsComplete))
                {
                    Debug.LogError("[QUEST-COMPLETE] state=objectivesIncomplete");
                    return;
                }
                var questData = DatabaseLoader.Quests.FirstOrDefault(q =>
                    q.id.Equals(activeQuest.QuestId, StringComparison.OrdinalIgnoreCase));
                int xp = questData?.rewards?.experience ?? 0;
                int gold = questData?.rewards?.gold ?? 0;
                Debug.LogError($"[QUEST-COMPLETE] rewardXp={xp} rewardGold={gold}");
                var playerState = GetPlayerState(conn.ConnId.ToString());
                playerState.ActiveQuests.Remove(activeQuest);
                playerState.CompletedQuests.Add(activeQuest.QuestId);
                SendCompletePacket(conn, instanceId);
                SendFinalizePacket(conn, instanceId);
                SendRemovePacket(conn, instanceId);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[QUEST-COMPLETE] state=failed message='{ex.Message}'");
            }
        }


        public void SendAddPacket(RRConnection conn, QuestData questData, ActiveQuest activeQuest)
        {
            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x35);
            writer.WriteUInt16(conn.QuestManagerId);
            writer.WriteByte(0x01);

            writer.WriteByte(0x04);
            writer.WriteUInt32(DatabaseLoader.ComputeDJB2Hash(questData.id));

            writer.WriteUInt32(activeQuest.InstanceId);

            var objectives = activeQuest.Objectives ?? new List<QuestProgress>();

            bool allComplete = objectives.Count > 0 && objectives.All(o => o.IsComplete);
            writer.WriteByte(allComplete ? (byte)0x01 : (byte)0x00);

            writer.WriteByte((byte)objectives.Count);

            foreach (var obj in objectives)
            {
                byte flags = (byte)(0x02 | (obj.IsComplete ? 0x01 : 0x00));
                writer.WriteByte(flags);
                string addLabel = $"{obj.Label ?? "Objective"}: {obj.Current} / {(obj.Required > 0 ? obj.Required : 1)}";
                writer.WriteCString(addLabel);
                writer.WriteUInt16((ushort)(obj.Required > 0 ? obj.Required : 1));
            }

            if (!WriteEntitySynchAndEnd(conn, writer, "QUEST-ADD")) return;
            Debug.LogError($"[QUEST-ADD] hex={BitConverter.ToString(writer.ToArray()).Replace("-", " ")}");
            Debug.LogError($"[QUEST-ADD] Sending {questData.id} InstanceId={activeQuest.InstanceId} allComplete={allComplete}");
            _sendPacket?.Invoke(conn, 0x01, 0x0F, writer.ToArray());
        }

        public void SendRemovePacket(RRConnection conn, uint instanceId)
        {
            var writer = new LEWriter();
            writer.WriteByte(0x07);

            writer.WriteByte(0x35);
            writer.WriteUInt16(conn.QuestManagerId);
            writer.WriteByte(0x02);

            writer.WriteUInt32(instanceId);

            if (!WriteEntitySynchAndEnd(conn, writer, "QUEST-REMOVE")) return;
            Debug.LogError($"[QUEST-ADD] hex={BitConverter.ToString(writer.ToArray()).Replace("-", " ")}");
            Debug.LogError($"[QUEST-REMOVE] InstanceId={instanceId}");
            _sendPacket?.Invoke(conn, 0x01, 0x0F, writer.ToArray());
        }

        public void SendProgressPacket(RRConnection conn, uint instanceId, ActiveQuest quest)
        {
            var objectives = quest.Objectives ?? new System.Collections.Generic.List<QuestProgress>();
            bool allComplete = objectives.Count > 0 && objectives.All(o => o.IsComplete);

            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x35);
            writer.WriteUInt16(conn.QuestManagerId);
            writer.WriteByte(0x03);

            writer.WriteUInt32(instanceId);
            writer.WriteByte(0x01);

            writer.WriteByte((byte)objectives.Count);
            foreach (var obj in objectives)
            {
                byte flags = (byte)(0x02 | (obj.IsComplete ? 0x01 : 0x00));
                writer.WriteByte(flags);
                string progressLabel = $"{obj.Label ?? "Objective"}: {obj.Current} / {(obj.Required > 0 ? obj.Required : 1)}";
                writer.WriteCString(progressLabel);
                writer.WriteUInt16((ushort)(obj.Required > 0 ? obj.Required : 1));
            }

            if (!WriteEntitySynchAndEnd(conn, writer, "QUEST-PROGRESS")) return;
            Debug.LogError($"[QUEST-PROGRESS] Objectives packet: {BitConverter.ToString(writer.ToArray()).Replace("-", " ")}");
            _sendPacket?.Invoke(conn, 0x01, 0x0F, writer.ToArray());

            var flagWriter = new LEWriter();
            flagWriter.WriteByte(0x07);
            flagWriter.WriteByte(0x35);
            flagWriter.WriteUInt16(conn.QuestManagerId);
            flagWriter.WriteByte(0x03);

            flagWriter.WriteUInt32(instanceId);
            flagWriter.WriteByte(0x00);
            flagWriter.WriteByte(allComplete ? (byte)0x01 : (byte)0x00);

            if (!WriteEntitySynchAndEnd(conn, flagWriter, "QUEST-PROGRESS-FLAG")) return;
            Debug.LogError($"[QUEST-PROGRESS] Complete flag packet: allComplete={allComplete}");
            _sendPacket?.Invoke(conn, 0x01, 0x0F, flagWriter.ToArray());

            Debug.LogError($"[QUEST-PROGRESS] InstanceId={instanceId} allComplete={allComplete}");
        }

        public void SendCompletePacket(RRConnection conn, uint instanceId)
        {
            var writer = new LEWriter();
            writer.WriteByte(0x07);

            writer.WriteByte(0x35);
            writer.WriteUInt16(conn.QuestManagerId);
            writer.WriteByte(0x06);

            writer.WriteUInt32(instanceId);

            if (!WriteEntitySynchAndEnd(conn, writer, "QUEST-COMPLETE-PKT")) return;
            Debug.LogError($"[QUEST-ADD] hex={BitConverter.ToString(writer.ToArray()).Replace("-", " ")}");
            Debug.LogError($"[QUEST-COMPLETE-PKT] InstanceId={instanceId}");
            _sendPacket?.Invoke(conn, 0x01, 0x0F, writer.ToArray());
        }

        public void SendAvailableQuestUpdateForZone(RRConnection conn)
        {
            Debug.LogError($"[QUEST-AVAILABLE] zone={conn.CurrentZoneGcType} state=called");

            var playerState = GetPlayerState(conn.ConnId.ToString());
            if (playerState == null)
            {
                Debug.LogError($"[QUEST-AVAILABLE] No player state for {conn.ConnId}");
                return;
            }

            string currentZonePrefix = conn.CurrentZoneGcType?.ToLowerInvariant() ?? "world.town";
            Debug.LogError($"[QUEST-AVAILABLE] Zone prefix: {currentZonePrefix}");
            Debug.LogError($"[QUEST-AVAILABLE] Active quests: {playerState.ActiveQuests.Count}");
            var q11a1 = DatabaseLoader.Quests.FirstOrDefault(q => q.id == "world.dungeon00.quest.Q11_a1");
            if (q11a1 != null)
            {


                Debug.LogError($"[QUEST-DETAIL] Q11_a1 npc='{q11a1.npc}' npc2='{q11a1.npc2}'");

                bool isActive = playerState.ActiveQuests.Any(a => a.QuestId.Equals("world.dungeon00.quest.Q11_a1", StringComparison.OrdinalIgnoreCase));
                bool isCompleted = playerState.CompletedQuests.Contains("world.dungeon00.quest.Q11_a1");
                Debug.LogError($"[QUEST-DETAIL] Q11_a1 active={isActive} completed={isCompleted}");
            }
            var zoneQuestGivers = GetQuestGiversForCurrentZone(conn, currentZonePrefix);
            var availableByNpc = DatabaseLoader.Quests
             .Where(q => !playerState.ActiveQuests.Any(a => a.QuestId.Equals(q.id, StringComparison.OrdinalIgnoreCase)))
             .Where(q => !playerState.CompletedQuests.Any(c => c.Equals(q.id, StringComparison.OrdinalIgnoreCase)))
             .Where(q => string.IsNullOrEmpty(q.requiredQuest) || playerState.CompletedQuests.Any(c => c.Equals(q.requiredQuest, StringComparison.OrdinalIgnoreCase)))
             .Where(q => playerState.Level >= q.level && playerState.Level <= q.maxLevel)
             .SelectMany(q => GetQuestGiversForQuest(q, currentZonePrefix, zoneQuestGivers).Select(giver => new { Giver = giver, Quest = q }))
             .GroupBy(q => q.Giver, StringComparer.OrdinalIgnoreCase)
             .ToDictionary(g => g.Key, g => g.Select(x => x.Quest).ToList(), StringComparer.OrdinalIgnoreCase);


            Debug.LogError($"[QUEST-AVAILABLE] {availableByNpc.Count} NPCs with available quests");

            foreach (var npcQuestEntry in availableByNpc)
            {
                Debug.LogError($"[QUEST-AVAILABLE] NPC: {npcQuestEntry.Key} has {npcQuestEntry.Value.Count} available quests");
            }

            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x35);
            writer.WriteUInt16(conn.QuestManagerId);
            writer.WriteByte(0x07);

            writer.WriteByte((byte)availableByNpc.Count);

            foreach (var npcQuestEntry in availableByNpc)
            {
                string npcGcType = npcQuestEntry.Key;
                var quests = npcQuestEntry.Value;

                byte[] npcBytes = System.Text.Encoding.UTF8.GetBytes(npcGcType);
                writer.WriteBytes(npcBytes);
                writer.WriteByte(0x00);

                writer.WriteByte((byte)quests.Count);

                foreach (var quest in quests)
                {
                    uint hash = DatabaseLoader.ComputeDJB2Hash(quest.id);
                    writer.WriteByte(0x04);
                    writer.WriteUInt32(hash);
                    Debug.LogError($"[QUEST-AVAILABLE]   Quest: {quest.id} -> 0x{hash:X8}");
                }
            }

            if (!WriteEntitySynchAndEnd(conn, writer, "QUEST-AVAILABLE")) return;

            var packet = writer.ToArray();
            Debug.LogError($"[QUEST-AVAILABLE] Sending packet: {packet.Length} bytes");
            Debug.LogError($"[QUEST-AVAILABLE] hex={BitConverter.ToString(packet).Replace("-", " ")}");
            _sendPacket?.Invoke(conn, 0x01, 0x0F, packet);
        }

        private HashSet<string> GetQuestGiversForCurrentZone(RRConnection conn, string currentZonePrefix)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string zoneName = conn.CurrentZoneName ?? "";
            IEnumerable<NPCData> npcs = Enumerable.Empty<NPCData>();

            if (zoneName.IndexOf("pvp", StringComparison.OrdinalIgnoreCase) >= 0 || currentZonePrefix.Equals("world.pvp", StringComparison.OrdinalIgnoreCase))
                npcs = DatabaseLoader.PvpNPCs;
            else if (zoneName.IndexOf("tutorial", StringComparison.OrdinalIgnoreCase) >= 0 || currentZonePrefix.Equals("world.tutorial", StringComparison.OrdinalIgnoreCase))
                npcs = DatabaseLoader.TutorialNPCs;
            else if (zoneName.IndexOf("town", StringComparison.OrdinalIgnoreCase) >= 0 || currentZonePrefix.Equals("world.town", StringComparison.OrdinalIgnoreCase))
                npcs = DatabaseLoader.TownNPCs;

            foreach (var npc in npcs)
                if (!string.IsNullOrWhiteSpace(npc.gcType))
                    result.Add(npc.gcType);

            return result;
        }

        private IEnumerable<string> GetQuestGiversForQuest(QuestData quest, string currentZonePrefix, HashSet<string> zoneQuestGivers)
        {
            var result = new List<string>();
            foreach (var giver in EnumerateQuestGivers(quest))
            {
                if (string.IsNullOrWhiteSpace(giver)) continue;
                if (ShouldSuppressQuestForGiver(quest, giver, currentZonePrefix)) continue;
                if (!QuestGiverBelongsInCurrentZone(giver, currentZonePrefix, zoneQuestGivers)) continue;
                if (!result.Contains(giver, StringComparer.OrdinalIgnoreCase))
                    result.Add(giver);
            }
            return result;
        }

        private IEnumerable<string> EnumerateQuestGivers(QuestData quest)
        {
            if (!string.IsNullOrWhiteSpace(quest.npc))
                yield return quest.npc;
            if (!string.IsNullOrWhiteSpace(quest.npc2))
                yield return quest.npc2;
            if (string.Equals(quest.id, "quests.base.HelperNoobosaur.Q131_a1", StringComparison.OrdinalIgnoreCase))
            {
                yield return "world.town.npc.HelperNoobosaur01";
                yield return "world.town.npc.HelperNoobosaur01_pvp";
            }
        }

        private bool QuestGiverBelongsInCurrentZone(string giver, string currentZonePrefix, HashSet<string> zoneQuestGivers)
        {
            return zoneQuestGivers.Contains(giver) || giver.StartsWith(currentZonePrefix, StringComparison.OrdinalIgnoreCase);
        }

        private bool ShouldSuppressQuestForGiver(QuestData quest, string giver, string currentZonePrefix)
        {
            if (!string.Equals(quest.id, "quests.base.HelperNoobosaur.Q131_a1", StringComparison.OrdinalIgnoreCase))
                return false;
            return currentZonePrefix.Equals("world.tutorial", StringComparison.OrdinalIgnoreCase) ||
                   giver.Equals("world.tutorial.npc.HelperNoobosaur00", StringComparison.OrdinalIgnoreCase);
        }




        public void SendQueryResponse(RRConnection conn, uint questHash, uint npcEntityId)
        {
            if (!DatabaseLoader.QuestsByHash.TryGetValue(questHash, out var quest))
            {
                Debug.LogError($"[QUEST-QUERY] hash=0x{questHash:X8} state=unknown");
                return;
            }

            var playerState = GetPlayerState(conn.ConnId.ToString());

            var activeQuest = playerState?.ActiveQuests.FirstOrDefault(
                aq => aq.QuestId.Equals(quest.id, StringComparison.OrdinalIgnoreCase));

            bool isTurnIn = false;
            if (activeQuest != null)
            {
                var objectives = activeQuest.Objectives ?? new List<QuestProgress>();
                bool allComplete = CanQueryComplete(activeQuest);
                isTurnIn = allComplete;
                Debug.LogError($"[QUEST-QUERY] state=activeQuest instanceId={activeQuest.InstanceId} objectives={objectives.Count} allComplete={allComplete} isTurnIn={isTurnIn}");
            }
            else
            {
                Debug.LogError($"[QUEST-QUERY] quest={quest.id} state=noActiveQuest");
            }

            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x35);
            writer.WriteUInt16(conn.QuestManagerId);

            if (isTurnIn)
            {
                conn.PendingTurnInInstanceId = activeQuest.InstanceId;
                conn.PendingQuestHash = 0;

                writer.WriteByte(0x06);
                writer.WriteUInt32(activeQuest.InstanceId);
                Debug.LogError($"[QUEST-QUERY] action=sendTurnIn quest={quest.id} instanceId={activeQuest.InstanceId}");
            }
            else if (activeQuest != null)
            {
                Debug.LogError($"[QUEST-QUERY] quest={quest.id} state=activeIncomplete action=suppressAcceptDialog");
                return;
            }
            else
            {
                writer.WriteByte(0x04);
                writer.WriteByte(0x04);
                writer.WriteUInt32(questHash);
                Debug.LogError($"[QUEST-QUERY] action=sendAcceptDialog quest={quest.id} hash=0x{questHash:X8}");
            }

            if (!WriteEntitySynchAndEnd(conn, writer, "QUEST-QUERY")) return;

            Debug.LogError($"[QUEST-QUERY] hex={BitConverter.ToString(writer.ToArray()).Replace("-", " ")}");
            _sendPacket?.Invoke(conn, 0x01, 0x0F, writer.ToArray());
        }

        public void HandleAcceptConfirmed(RRConnection conn, uint npcEntityId, uint questHash)
        {
            if (!DatabaseLoader.QuestsByHash.TryGetValue(questHash, out var quest))
            {
                Debug.LogError($"[QUEST-ACCEPT] hash=0x{questHash:X8} state=unknown");
                return;
            }

            Debug.LogError($"[QUEST-ACCEPT] action=accept quest={quest.id}");
            string npcId = conn.CurrentDialogNpcId ?? $"npc_{npcEntityId}";
            var result = AcceptQuest(conn.ConnId.ToString(), quest.id, npcId);

            if (result.Success)
            {
                result.Quest.InstanceId = conn.NextQuestInstanceId++;

                if (!string.IsNullOrEmpty(quest.onAcceptItem))
                {
                    Debug.LogError($"[QUEST-ACCEPT] action=giveOnAcceptItem item={quest.onAcceptItem}");

                    foreach (var obj in result.Quest.Objectives)
                    {
                        if (obj.Type == "item" && obj.Target.Equals(quest.onAcceptItem, StringComparison.OrdinalIgnoreCase))
                        {
                            obj.Current = obj.Required;
                            Debug.LogError($"[QUEST-ACCEPT] action=autoComplete objective='{obj.Label}'");
                        }
                    }
                }

                SendAddPacket(conn, quest, result.Quest);
                SendAvailableQuestUpdateForZone(conn);
            }
        }





        public void SendFinalizePacket(RRConnection conn, uint instanceId)
        {
            var writer = new LEWriter();
            writer.WriteByte(0x07);

            writer.WriteByte(0x35);
            writer.WriteUInt16(conn.QuestManagerId);
            writer.WriteByte(0x08);

            writer.WriteUInt32(instanceId);

            if (!WriteEntitySynchAndEnd(conn, writer, "QUEST-FINALIZE")) return;
            Debug.LogError($"[QUEST-ADD] hex={BitConverter.ToString(writer.ToArray()).Replace("-", " ")}");
            Debug.LogError($"[QUEST-FINALIZE] InstanceId={instanceId}");
            _sendPacket?.Invoke(conn, 0x01, 0x0F, writer.ToArray());
        }

        public List<QuestProgressUpdate> OnCreatureKilled(RRConnection conn, string creatureGcType)
        {
            return OnCreatureKilled(conn, new List<string> { creatureGcType });
        }

        public List<QuestProgressUpdate> OnCreatureKilled(RRConnection conn, List<string> candidateGcTypes)
        {
            var updates = UpdateProgress(conn.ConnId.ToString(), "kill", candidateGcTypes);

            var sentQuestIds = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var update in updates)
            {
                if (!sentQuestIds.Add(update.QuestId)) continue;
                var quest = GetPlayerState(conn.ConnId.ToString())?.ActiveQuests
                    .FirstOrDefault(q => q.QuestId == update.QuestId);
                if (quest != null)
                    SendProgressPacket(conn, quest.InstanceId, quest);
            }

            return updates;
        }

        public List<QuestProgressUpdate> OnItemPickedUp(RRConnection conn, string itemGcType)
        {
            var updates = UpdateProgress(conn.ConnId.ToString(), "item", itemGcType);

            var sentQuestIds = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var update in updates)
            {
                if (!sentQuestIds.Add(update.QuestId)) continue;
                var quest = GetPlayerState(conn.ConnId.ToString())?.ActiveQuests
                    .FirstOrDefault(q => q.QuestId == update.QuestId);
                if (quest != null)
                    SendProgressPacket(conn, quest.InstanceId, quest);
            }

            return updates;
        }

        private List<QuestProgressUpdate> UpdateProgress(string connId, string eventType, string target)
        {
            return UpdateProgress(connId, eventType, new List<string> { target });
        }

        private List<QuestProgressUpdate> UpdateProgress(string connId, string eventType, List<string> candidateTargets)
        {
            var updates = new List<QuestProgressUpdate>();
            var playerState = GetPlayerState(connId);
            if (playerState == null) return updates;

            bool isKillEvent = "kill".Equals(eventType, StringComparison.OrdinalIgnoreCase);

            foreach (var quest in playerState.ActiveQuests)
            {
                foreach (var objective in quest.Objectives.Where(o => !o.IsComplete))
                {
                    if (!objective.Type.Equals(eventType, StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool anyMatch = false;

                    if (!string.IsNullOrEmpty(objective.Target))
                    {
                        foreach (var candidate in candidateTargets)
                        {
                            if (string.IsNullOrEmpty(candidate)) continue;
                            if (objective.Target.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                            {
                                anyMatch = true;
                                break;
                            }
                        }
                    }
                    else if (isKillEvent && !string.IsNullOrEmpty(objective.Label))
                    {
                        if (GetKillGCByLabel().TryGetValue(objective.Label, out var allowedMonsters))
                        {
                            foreach (var candidate in candidateTargets)
                            {
                                if (string.IsNullOrEmpty(candidate)) continue;
                                if (allowedMonsters.Contains(candidate))
                                {
                                    anyMatch = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (!anyMatch) continue;

                    objective.Current++;
                    updates.Add(new QuestProgressUpdate
                    {
                        QuestId = quest.QuestId,
                        ObjectiveName = objective.ObjectiveName,
                        Current = objective.Current,
                        Required = objective.Required,
                        IsComplete = objective.IsComplete,
                        Label = objective.Label
                    });
                    Debug.LogError($"[QUEST-MANAGER] action=progress label='{objective.Label}' current={objective.Current} required={objective.Required}");
                }
            }
            return updates;
        }

        public void SendTurnInDialog(RRConnection conn, uint instanceId)
        {
            var playerState = GetPlayerState(conn.ConnId.ToString());
            var activeQuest = playerState?.ActiveQuests.FirstOrDefault(q => q.InstanceId == instanceId);

            if (activeQuest == null)
            {
                Debug.LogError($"[QUEST-TURNIN] instanceId={instanceId} state=notFound");
                return;
            }

            uint questHash = DatabaseLoader.ComputeDJB2Hash(activeQuest.QuestId);
            Debug.LogError($"[QUEST-TURNIN] action=dialog quest={activeQuest.QuestId} hash=0x{questHash:X8}");

            conn.PendingTurnInInstanceId = instanceId;
            conn.PendingQuestHash = 0;

            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x35);
            writer.WriteUInt16(conn.QuestManagerId);
            writer.WriteByte(0x06);
            writer.WriteUInt32(instanceId);
            if (!WriteEntitySynchAndEnd(conn, writer, "QUEST-TURNIN")) return;

            var packet = writer.ToArray();
            Debug.LogError($"[QUEST-TURNIN] hex={BitConverter.ToString(packet).Replace("-", " ")}");
            _sendPacket?.Invoke(conn, 0x01, 0x0F, packet);
        }

        public void HandleTurnInConfirmed(RRConnection conn, uint instanceId)
        {
            var playerState = GetPlayerState(conn.ConnId.ToString());
            var activeQuest = playerState?.ActiveQuests.FirstOrDefault(q => q.InstanceId == instanceId);

            if (activeQuest == null)
            {
                Debug.LogError($"[QUEST-TURNIN] instanceId={instanceId} state=notFound");
                return;
            }

            Debug.LogError($"[QUEST-TURNIN] action=complete quest={activeQuest.QuestId}");

            var questData = DatabaseLoader.Quests.FirstOrDefault(q =>
                q.id.Equals(activeQuest.QuestId, StringComparison.OrdinalIgnoreCase));

            if (questData != null)
            {
                int xp = questData.rewards?.experience ?? 0;
                int gold = questData.rewards?.gold ?? 0;
                Debug.LogError($"[QUEST-TURNIN] rewardGold={gold} rewardXp={xp}");
            }

            playerState.ActiveQuests.Remove(activeQuest);
            playerState.CompletedQuests.Add(activeQuest.QuestId);

            SendFinalizePacket(conn, instanceId);
            SendRemovePacket(conn, instanceId);
            SendAvailableQuestUpdateForZone(conn);

            Debug.LogError($"[QUEST-TURNIN] state=complete active={playerState.ActiveQuests.Count} completed={playerState.CompletedQuests.Count}");
        }






        public QuestTurnInResult TurnInQuest(string connId, string questId)
        {
            Debug.LogError($"[QUEST-MANAGER] action=turnIn quest={questId}");
            var result = new QuestTurnInResult { Success = false };

            var playerState = GetPlayerState(connId);
            if (playerState == null) return result;

            var activeQuest = playerState.ActiveQuests.FirstOrDefault(q =>
                q.QuestId.Equals(questId, StringComparison.OrdinalIgnoreCase));
            if (activeQuest == null) return result;

            if (!activeQuest.Objectives.All(o => o.IsComplete)) return result;

            var questData = DatabaseLoader.Quests.FirstOrDefault(q =>
                q.id.Equals(questId, StringComparison.OrdinalIgnoreCase));

            if (questData?.rewards != null)
            {
                result.ExperienceReward = questData.rewards.experience;
                result.GoldReward = questData.rewards.gold;
            }

            playerState.ActiveQuests.Remove(activeQuest);
            playerState.CompletedQuests.Add(questId);
            result.Success = true;
            result.FollowupQuestId = questData?.followupQuest;
            return result;
        }

        public bool UnlockCheckpoint(string connId, string checkpointId)
        {
            var playerState = GetPlayerState(connId);
            if (playerState == null) return false;
            if (playerState.UnlockedCheckpoints.Contains(checkpointId)) return false;
            playerState.UnlockedCheckpoints.Add(checkpointId);
            return true;
        }

        public void WriteQuestManagerComponent(LEWriter writer, string connId, ushort playerId,
                                                ushort questManagerId, Action<LEWriter, string, bool> writeGcType,
                                                RRConnection conn = null)
        {
            var playerState = GetPlayerState(connId);
            var activeQuests = playerState?.ActiveQuests ?? new List<ActiveQuest>();
            var checkpoints = playerState?.UnlockedCheckpoints ?? new List<string>();

            writer.WriteByte(0x32);
            writer.WriteUInt16(playerId);
            writer.WriteUInt16(questManagerId);
            writeGcType(writer, "QuestManager", false);
            writer.WriteByte(0x01);

            writer.WriteUInt32(0x01);

            if (conn != null && conn.HasSavedTownPortal)
            {
                writer.WriteByte(0x01);
                writer.WriteCString(conn.TownPortalZoneName);
                writer.WriteCString("");
                writer.WriteUInt32(conn.TownPortalZoneId);
                Debug.LogError($"[QM-INIT] Town portal saved: zone={conn.TownPortalZoneName} guid={conn.TownPortalZoneId}");
            }
            else
            {
                writer.WriteByte(0x00);
                writer.WriteCString("Hello");
                writer.WriteCString("HelloAgain");
                writer.WriteUInt32(0x00);
            }

            writer.WriteByte(0x00);
            writer.WriteCString("");
            writer.WriteCString("");
            writer.WriteUInt32(0x00);
            writer.WriteCString(conn?.ZonePortalSource ?? "");
            writer.WriteCString("");
            writer.WriteCString("");

            writer.WriteByte(0x00);

            ushort activeQuestCount = (ushort)activeQuests.Count;
            writer.WriteUInt16(activeQuestCount);

            foreach (var quest in activeQuests)
            {
                writeGcType(writer, quest.QuestId, true);
                writer.WriteUInt32(quest.InstanceId);
                bool allDone = quest.Objectives.Count > 0 && quest.Objectives.All(o => o.IsComplete);
                writer.WriteByte(allDone ? (byte)0x01 : (byte)0x00);
                writer.WriteByte((byte)quest.Objectives.Count);
                foreach (var obj in quest.Objectives)
                {
                    byte flags = (byte)(0x02 | (obj.IsComplete ? 0x01 : 0x00));
                    writer.WriteByte(flags);
                    string initLabel = $"{obj.Label ?? "Objective"}: {obj.Current} / {(obj.Required > 0 ? obj.Required : 1)}";
                    writer.WriteCString(initLabel);
                    writer.WriteUInt16((ushort)(obj.Required > 0 ? obj.Required : 1));
                }
            }

            ushort checkpointCount = (ushort)checkpoints.Count;
            writer.WriteUInt16(checkpointCount);
            foreach (var cp in checkpoints)
            {
                writeGcType(writer, cp, true);
            }
        }
    }

    public class PlayerQuestState
    {
        public string ConnId;
        public int Level = 1;
        public List<ActiveQuest> ActiveQuests = new List<ActiveQuest>();
        public List<string> CompletedQuests = new List<string>();
        public List<string> UnlockedCheckpoints = new List<string>();
    }

    [Serializable]
    public class ActiveQuest
    {
        public string QuestId;
        public string QuestGiverId;
        public DateTime AcceptedAt;
        public List<QuestProgress> Objectives = new List<QuestProgress>();
        public uint InstanceId;
    }

    [Serializable]
    public class QuestProgress
    {
        public string ObjectiveName;
        public string Type;
        public string Target;
        public string Label;
        public int Required;
        public int Current;
        public bool IsComplete => Current >= Required;
    }

    public class QuestAcceptResult
    {
        public bool Success;
        public string ErrorMessage;
        public ActiveQuest Quest;
        public QuestData QuestData;
    }

    public class QuestTurnInResult
    {
        public bool Success;
        public string ErrorMessage;
        public int ExperienceReward;
        public int GoldReward;
        public string FollowupQuestId;
    }

    public class QuestProgressUpdate
    {
        public string QuestId;
        public string ObjectiveName;
        public string Label;
        public int Current;
        public int Required;
        public bool IsComplete;
    }
}
