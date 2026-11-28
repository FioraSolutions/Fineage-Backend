import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import javax.xml.parsers.DocumentBuilderFactory;
import org.w3c.dom.Document;
import org.w3c.dom.Element;
import org.w3c.dom.Node;
import org.w3c.dom.NodeList;

/**
 * Formula-level Mobius CT0 oracle for the Fineage differential harness.
 *
 * <p>This is intentionally not a full GameServer run. It reads the Mobius reference XML
 * (class stats, item stats, skill funcs, NPC stats, statBonus.xml) and evaluates the pure
 * formula chain cited from {@code Formulas.java}, {@code PlayerStat.java},
 * {@code CreatureStat.java} and the registered {@code Func*} classes. GPL implementation
 * code is not copied; this is an independent evaluator of the documented behavior.</p>
 */
public final class MobiusFormulaDriver
{
    public static void main(String[] args) throws Exception
    {
        Map<String, String> opt = parseArgs(args);
        Path ref = Paths.get(require(opt, "ref"));
        String referenceLabel = opt.getOrDefault("reference-label", ref.toString());
        String scenario = require(opt, "scenario");
        Path dataDir = opt.containsKey("data") && !opt.get("data").isBlank() ? Paths.get(opt.get("data")) : null;
        DataSource source = DataSource.open(ref, dataDir);

        String attackerKind = kind(opt.getOrDefault("attacker-kind", "player"));
        String targetKind = kind(opt.getOrDefault("target-kind", "npc"));
        boolean matrix = Boolean.parseBoolean(opt.getOrDefault("matrix", "false"));
        boolean statsMatrix = Boolean.parseBoolean(opt.getOrDefault("stats-matrix", "false"));
        boolean magicMatrix = Boolean.parseBoolean(opt.getOrDefault("magic-matrix", "false"));
        String side = opt.getOrDefault("side", "front");
        boolean soulshot = Boolean.parseBoolean(opt.getOrDefault("soulshot", "false"));
        String cpSplitSpec = opt.getOrDefault("cp-splits", "");

        ActorStats attacker = resolveActor(source, attackerKind,
            intOpt(opt, "class-id", 0), intOpt(opt, "level", 0), intOpt(opt, "attacker-npc", 0),
            opt.getOrDefault("gear", ""), opt.getOrDefault("buffs", ""), opt.getOrDefault("passives", ""));
        ActorStats target = resolveActor(source, targetKind,
            intOpt(opt, "target-class-id", 0), intOpt(opt, "target-level", 0), intOpt(opt, "npc", 0),
            opt.getOrDefault("target-gear", ""), opt.getOrDefault("target-buffs", ""), "");

        double prox = proximity(side);
        int hitChance = hitChance(attacker.accuracy, target.evasion, 1.0);
        int critChance = Math.min(attacker.critPermille, 500);
        double damageNoCritRaw = 76.0 * Math.max(0.0, attacker.pAtk) * prox / Math.max(1.0, target.pDef);
        int damageNoCrit = Math.max(1, (int) damageNoCritRaw);
        double damageMean = (hitChance / 1000.0) * damageNoCritRaw * (1.0 + (critChance / 1000.0));

        Map<String, Double> metrics = new LinkedHashMap<>();
        metrics.put("pAtk", attacker.pAtk);
        metrics.put("pDef", attacker.pDef);
        metrics.put("pAtkSpd", matrix || statsMatrix || magicMatrix ? attacker.pAtkSpdRaw : (double) Math.round(attacker.pAtkSpdRaw));
        metrics.put("accuracy", attacker.accuracy);
        metrics.put("evasion", attacker.evasion);
        metrics.put("critPermille", (double) attacker.critPermille);
        metrics.put("hitChancePermille", (double) hitChance);
        metrics.put("critChancePermille", (double) critChance);
        metrics.put("damageNoCrit", (double) damageNoCrit);
        if (!cpSplitSpec.isBlank() || !matrix)
        {
            // damageMean is only emitted for the original statistical pilots.
        }
        if (!matrix)
            metrics.put("damageMeanPerSwing", damageMean);
        for (Map.Entry<String, int[]> split : readCpSplits(cpSplitSpec).entrySet())
        {
            metrics.put("cpLoss_" + split.getKey(), (double) split.getValue()[0]);
            metrics.put("hpLoss_" + split.getKey(), (double) split.getValue()[1]);
        }

        if (matrix)
            addMatrix(metrics, attacker, target, opt);
        if (statsMatrix)
            addStatMatrix(metrics, attacker);
        if (magicMatrix)
            addMagicMatrix(metrics, attacker, target, source, opt);

        String out = opt.get("out");
        String json = toJson(scenario, referenceLabel, metrics, !cpSplitSpec.isBlank() || hasKey(metrics, "cpLoss_front"), matrix || statsMatrix || magicMatrix);
        if (out == null)
            System.out.println(json);
        else
            Files.writeString(Paths.get(out), json, StandardCharsets.UTF_8);
    }

    private static boolean hasKey(Map<String, Double> metrics, String key)
    {
        return metrics.containsKey(key);
    }

    private static int intOpt(Map<String, String> opt, String key, int fallback)
    {
        String value = opt.get(key);
        if (value == null || value.isBlank())
            return fallback;
        return Integer.parseInt(value.trim());
    }

    private static String kind(String raw)
    {
        if (raw == null || raw.isBlank())
            return "player";
        String value = raw.trim().toLowerCase(java.util.Locale.ROOT);
        if (value.equals("player") || value.equals("npc") || value.equals("summon"))
            return value;
        throw new IllegalArgumentException("kind must be player, npc or summon: " + raw);
    }

    private static boolean playable(String kind)
    {
        return kind.equals("player") || kind.equals("summon");
    }

    private static int hitChance(double accuracy, double evasion, double conditionBonus)
    {
        int chance = (int) ((80 + (2 * (accuracy - evasion))) * 10);
        chance = (int) (chance * conditionBonus);
        return Math.max(200, Math.min(980, chance));
    }

    private static int physDam(double pAtk, double pDef, double proximity)
    {
        return Math.max(1, (int) (76.0 * Math.max(0.0, pAtk) * proximity / Math.max(1.0, pDef)));
    }

    private static int critDam(ActorStats attacker, double pDef, double proximity, boolean soulshot)
    {
        double defence = Math.max(1.0, pDef);
        double damage = Math.max(0.0, attacker.pAtk) * (soulshot ? 2.0 : 1.0);
        damage = 2 * attacker.critDamageMod * ((76.0 * damage * proximity) / defence);
        if (attacker.critDamageAdd > 0)
            damage += attacker.critDamageAdd * 77.0 / defence;
        return Math.max(1, (int) damage);
    }

    private static int attackTimeMs(double pAtkSpd)
    {
        return Math.max(100, (int) (500000.0 / Math.max(1.0, pAtkSpd)));
    }

    private static int weaponReuseMs(int reuseDelayMs, double pAtkSpd)
    {
        if (reuseDelayMs <= 0)
            return 0;
        return (int) ((reuseDelayMs * 333.0) / Math.max(1.0, pAtkSpd));
    }

    private static void addMatrix(Map<String, Double> metrics, ActorStats attacker, ActorStats target, Map<String, String> opt)
    {
        int reuse = attacker.bow && attacker.reuseDelayMs == 0 ? 1500 : attacker.reuseDelayMs;
        metrics.put("targetPDef", target.pDef);
        metrics.put("targetEvasion", target.evasion);
        metrics.put("targetAccuracy", target.accuracy);
        metrics.put("attackRange", (double) attacker.attackRange);
        metrics.put("attackTimeMs", (double) attackTimeMs(attacker.pAtkSpdRaw));
        metrics.put("attackIntervalMs", (double) (attackTimeMs(attacker.pAtkSpdRaw) + weaponReuseMs(reuse, attacker.pAtkSpdRaw)));
        metrics.put("playableAttacker", attacker.playable ? 1.0 : 0.0);
        metrics.put("pvp", (attacker.playable && target.playable) ? 1.0 : 0.0);
        metrics.put("damageFront", (double) physDam(attacker.pAtk, target.pDef, 1.0));
        metrics.put("damageSide", (double) physDam(attacker.pAtk, target.pDef, 1.1));
        metrics.put("damageBehind", (double) physDam(attacker.pAtk, target.pDef, 1.2));
        metrics.put("damageSoulshotFront", (double) physDam(attacker.pAtk * 2, target.pDef, 1.0));
        metrics.put("damageCritFront", (double) critDam(attacker, target.pDef, 1.0, false));
        metrics.put("damageCritSoulshotFront", (double) critDam(attacker, target.pDef, 1.0, true));
        int baseHit = (int) ((80 + (2 * (attacker.accuracy - target.evasion))) * 10);
        metrics.put("hitChanceFront", (double) hitChance(attacker.accuracy, target.evasion, 1.0));
        metrics.put("hitChanceSide", (double) hitChance(attacker.accuracy, target.evasion, 1.05));
        metrics.put("hitChanceBehind", (double) hitChance(attacker.accuracy, target.evasion, 1.10));
        metrics.put("hitChanceBaseUnclamped", (double) baseHit);
        if (target.shieldDef > 0 && target.shieldRate > 1e-6)
        {
            metrics.put("shieldDef", (double) target.shieldDef);
            metrics.put("shieldBlockRate", target.shieldRate);
            metrics.put("shieldBlockRateVsBow", target.shieldRate * 1.3);
            metrics.put("damageShieldFront", (double) physDam(attacker.pAtk, target.pDef + target.shieldDef, 1.0));
            metrics.put("damagePerfectBlock", 1.0);
        }
        if (attacker.bow)
        {
            metrics.put("bowRange", (double) attacker.attackRange);
            metrics.put("bowReuseMs", (double) weaponReuseMs(reuse, attacker.pAtkSpdRaw));
        }
        if ("player".equals(kind(opt.getOrDefault("target-kind", ""))))
        {
            int front = physDam(attacker.pAtk, target.pDef, 1.0);
            int hp = intOpt(opt, "target-hp", 200);
            int cp = intOpt(opt, "target-cp", 200);
            int[] playableSplit = cpSplit(front, hp, cp, attacker.playable, false);
            metrics.put("cpLoss_front", (double) playableSplit[0]);
            metrics.put("hpLoss_front", (double) playableSplit[1]);
            int[] bypass = cpSplit(front, hp, cp, false, false);
            metrics.put("cpLoss_npcBypass", (double) bypass[0]);
            metrics.put("hpLoss_npcBypass", (double) bypass[1]);
        }
    }

    /**
     * {@code Formulas.calcMagicDam} / {@code calcMagicSuccess} cells. The mAtk / mDef inputs are the folded stats of
     * the measured actors; shots multiply mAtk exactly as {@code mAtk *= bss ? 4 : sps ? 2 : 1}.
     */
    private static void addMagicMatrix(Map<String, Double> metrics, ActorStats attacker, ActorStats target,
        DataSource source, Map<String, String> opt) throws Exception
    {
        int skillId = intOpt(opt, "magic-skill", 0);
        int skillLevel = intOpt(opt, "magic-level", 1);
        double critPower = Double.parseDouble(opt.getOrDefault("magic-crit-power", "1"));
        double[] skill = source.readMagicSkill(skillId, skillLevel);
        double power = skill[0];
        int magicLevel = (int) skill[1];
        if (magicLevel <= 0)
            magicLevel = attacker.level;

        metrics.put("magicPower", power);
        metrics.put("magicLevel", (double) magicLevel);
        metrics.put("casterLevel", (double) attacker.level);
        metrics.put("targetLevel", (double) target.level);
        metrics.put("mAtk", attacker.mAtk);
        metrics.put("targetMDef", target.mDef);

        // calcMagicSuccess: 100 - round((float) 1.3^(targetLevel - magicLevel)).
        int rate = 100 - Math.round((float) Math.pow(1.3, target.level - magicLevel));
        metrics.put("magicSuccessRate", (double) rate);
        metrics.put("magicHalfFailEligible", target.level - attacker.level <= 9 ? 1.0 : 0.0);

        boolean pvp = target.kindPlayer;
        double dNormal = magicBase(attacker.mAtk, target.mDef, power);
        double dSps = magicBase(attacker.mAtk * 2, target.mDef, power);
        double dBss = magicBase(attacker.mAtk * 4, target.mDef, power);
        metrics.put("damageNormal", (double) truncate(dNormal));
        metrics.put("damageSps", (double) truncate(dSps));
        metrics.put("damageBss", (double) truncate(dBss));
        metrics.put("damageCrit", (double) truncate(dNormal * 3 * critPower));
        metrics.put("damageCritBss", (double) truncate(dBss * 3 * critPower));
        // calcMagicDam: crit is x2.5 only when attacker AND target are players (the PvP probe cell is player vs player).
        metrics.put("damageCritPvp", (double) truncate(dNormal * 2.5 * critPower));
        metrics.put("damageHalfFail", (double) truncate(dNormal / 2));
        metrics.put("damageResisted", 1.0);
        if (target.shieldDef > 0)
        {
            metrics.put("damageShieldBlock", (double) truncate(magicBase(attacker.mAtk, target.mDef + target.shieldDef, power)));
            metrics.put("damagePerfectBlock", 1.0);
        }

        // getRandomDamageMultiplier: weapon RANDOM_DAMAGE, or 5 + (int) sqrt(level) unarmed.
        int random = attacker.armed ? attacker.randomDamage : 5 + (int) Math.sqrt(attacker.level);
        metrics.put("randomPercent", (double) random);
        metrics.put("damageRandomMin", (double) truncate(dNormal * (1 + (double) -random / 100)));
        metrics.put("damageRandomMax", (double) truncate(dNormal * (1 + (double) random / 100)));
    }

    private static double magicBase(double mAtk, double mDef, double power)
    {
        return ((91 * Math.sqrt(mAtk)) / mDef) * power;
    }

    /** {@code (int) damage}; the MagicalDamage effect does nothing at 0. */
    private static int truncate(double damage)
    {
        return Math.max(0, (int) damage);
    }

    private static int[] cpSplit(int damage, int hp, int cp, boolean playable, boolean ignoreCp)
    {
        int amount = Math.max(0, damage);
        int cpLoss = (playable && !ignoreCp) ? Math.min(Math.max(0, cp), amount) : 0;
        amount -= cpLoss;
        int hpLoss = Math.min(Math.max(0, hp), amount);
        return new int[] { cpLoss, hpLoss };
    }

    private static ActorStats resolveActor(DataSource source, String kind, int classId, int level, int npcId,
        String gearSpec, String buffSpec, String passiveSpec) throws Exception
    {
        if (kind.equals("player"))
            return measurePlayer(source, classId, level, gearSpec, buffSpec, passiveSpec);
        return measureNpc(source, npcId, playable(kind));
    }

    private static ActorStats measurePlayer(DataSource source, int classId, int level, String gearSpec, String buffSpec,
        String passiveSpec) throws Exception
    {
        if (level < 1)
            throw new IllegalArgumentException("player level must be positive");
        ClassData cls = source.readClass(classId);
        Map<String, ItemData> gear = source.readGear(gearSpec);
        StatBonus bonus = source.readStatBonus();
        List<BuffFunc> buffs = source.readBuffs(buffSpec);
        double strBonus = bonus.bonus("STR", cls.str);
        double dexBonus = bonus.bonus("DEX", cls.dex);
        double conBonus = bonus.bonus("CON", cls.con);
        double intBonus = bonus.bonus("INT", cls.intel);
        double witBonus = bonus.bonus("WIT", cls.wit);
        double menBonus = bonus.bonus("MEN", cls.men);
        double levelMod = (level + 89) / 100.0;

        ItemData weapon = gear.get("rhand");
        if (weapon == null)
            weapon = gear.get("lrhand");
        if (weapon != null && weapon.pAtk <= 0 && weapon.pAtkSpd <= 0 && !weapon.bow)
            weapon = null;

        double weaponPAtk = weapon != null && weapon.pAtk > 0
            ? weapon.pAtk + enchantPhysical(weapon) : cls.basePAtk;
        double weaponMAtk = weapon != null && weapon.mAtk > 0
            ? weapon.mAtk + enchantMagic(weapon) : cls.baseMAtk;
        double pAtk = weaponPAtk * strBonus * levelMod;
        double pDef = (cls.basePDefSum + gearPDefDelta(cls, gear)) * levelMod;
        double mAtk = weaponMAtk * intBonus * intBonus * levelMod * levelMod;
        double mDef = Math.max(1.0, cls.baseMDef * menBonus * levelMod);
        double pAtkSpdBase = weapon != null && weapon.pAtkSpd > 0 ? weapon.pAtkSpd : cls.basePAtkSpd;
        double pAtkSpd = pAtkSpdBase * dexBonus;
        double mAtkSpd = Math.max(1.0, cls.baseMAtkSpd * witBonus);
        double critBeast = weapon != null && weapon.critRate > 0 ? weapon.critRate : cls.baseCritRate;
        double crit = critBeast * dexBonus * 10.0;
        double accuracy = (int) (Math.sqrt(Math.max(0, cls.dex)) * 6.0 + Math.max(1, level));
        double evasion = accuracy;

        Map<String, Double> stats = new HashMap<>();
        stats.put("pAtk", pAtk);
        stats.put("pDef", pDef);
        stats.put("pAtkSpd", pAtkSpd);
        stats.put("mAtk", mAtk);
        stats.put("mDef", mDef);
        stats.put("mAtkSpd", mAtkSpd);
        stats.put("critRate", crit);
        stats.put("accuracy", accuracy);
        stats.put("evasion", evasion);
        applyBuffs(stats, buffs);
        applyBuffs(stats, source.readPassives(passiveSpec, gear));
        pAtk = stats.get("pAtk");
        pDef = stats.get("pDef");
        pAtkSpd = stats.get("pAtkSpd");
        mAtk = stats.get("mAtk");
        mDef = stats.get("mDef");
        mAtkSpd = stats.get("mAtkSpd");
        crit = stats.get("critRate");
        accuracy = stats.get("accuracy");
        evasion = stats.get("evasion");

        ActorStats a = new ActorStats();
        a.pAtk = pAtk;
        a.pDef = Math.max(1.0, pDef);
        a.pAtkSpdRaw = pAtkSpd;
        a.accuracy = accuracy;
        a.evasion = evasion;
        ItemData shield = gear.get("lhand");
        if (shield != null && shield.shieldDef > 0)
        {
            a.shieldDef = shield.shieldDef;
            a.shieldRate = shield.shieldRate * dexBonus;
            a.evasion = Math.max(0, a.evasion - shield.shieldEvasionPenalty);
        }
        a.critPermille = (int) (Math.min(crit, 500.0) + 0.5);
        a.attackRange = weapon != null && weapon.attackRange > 0 ? weapon.attackRange : 40;
        a.randomDamage = weapon != null && weapon.randomDamage > 0 ? weapon.randomDamage : 0;
        a.playable = true;
        a.bow = weapon != null && weapon.bow;
        a.reuseDelayMs = a.bow ? 1500 : 0;
        a.critDamageMod = 1.0;
        a.critDamageAdd = 0.0;
        a.str = cls.str;
        a.dex = cls.dex;
        a.con = cls.con;
        a.intel = cls.intel;
        a.wit = cls.wit;
        a.men = cls.men;
        a.strBonus = strBonus;
        a.dexBonus = dexBonus;
        a.conBonus = conBonus;
        a.intBonus = intBonus;
        a.witBonus = witBonus;
        a.menBonus = menBonus;
        a.mAtk = mAtk;
        a.mDef = Math.max(1.0, mDef);
        a.mAtkSpd = mAtkSpd;
        double magicCritBase = weapon != null ? witBonus : 1.0;
        a.mCritPermille = (int) magicCritBase * 10;
        int idx = Math.max(0, Math.min(level, cls.hp.length) - 1);
        a.maxHp = (int) (cls.hp[idx] * conBonus);
        a.maxMp = (int) (cls.mp[idx] * menBonus);
        a.maxCp = (int) (cls.cp[idx] * conBonus);
        a.level = level;
        a.armed = weapon != null;
        a.kindPlayer = true;
        return a;
    }

    private static ActorStats measureNpc(DataSource source, int npcId, boolean playable) throws Exception
    {
        if (npcId <= 0)
            throw new IllegalArgumentException("npc/summon id must be positive");
        NpcData npc = source.readNpc(npcId);
        StatBonus bonus = source.readStatBonus();
        double dexBonus = bonus.bonus("DEX", npc.dex);
        ActorStats a = new ActorStats();
        a.pAtk = npc.pAtk;
        a.pDef = Math.max(1.0, npc.pDef);
        a.pAtkSpdRaw = Math.max(1, npc.attackSpeed);
        a.accuracy = npc.accuracy + Math.sqrt(Math.max(0, npc.dex)) * 6.0 + Math.max(1, npc.level);
        a.evasion = npc.evasion + Math.sqrt(Math.max(0, npc.dex)) * 6.0 + Math.max(1, npc.level);
        a.critPermille = (int) (npc.critical * 10 * dexBonus);
        a.attackRange = npc.attackRange > 0 ? npc.attackRange : 40;
        a.randomDamage = npc.randomDamage;
        a.playable = playable;
        a.bow = npc.bow;
        a.reuseDelayMs = a.bow ? 1500 : 0;
        a.shieldDef = npc.shieldDef;
        a.shieldRate = npc.shieldRate * dexBonus;
        // FuncMAtkMod / FuncMDefMod on a non-player: mAtk * INT.bonus^2 * levelMod^2, mDef * MEN.bonus * levelMod.
        double levelMod = (npc.level + 89) / 100.0;
        double intBonus = bonus.bonus("INT", npc.intel);
        double menBonus = bonus.bonus("MEN", npc.men);
        a.mAtk = npc.mAtk * intBonus * intBonus * levelMod * levelMod;
        a.mDef = Math.max(1.0, npc.mDef * menBonus * levelMod);
        a.level = npc.level;
        a.armed = false;
        return a;
    }

    private static Map<String, String> parseArgs(String[] args)
    {
        Map<String, String> out = new HashMap<>();
        for (int i = 0; i < args.length; i++)
        {
            if (!args[i].startsWith("--"))
                throw new IllegalArgumentException("Unexpected argument: " + args[i]);
            String key = args[i].substring(2);
            if (i + 1 >= args.length)
                throw new IllegalArgumentException("Missing value for --" + key);
            out.put(key, args[++i]);
        }
        return out;
    }

    private static String require(Map<String, String> opt, String key)
    {
        String value = opt.get(key);
        if (value == null || value.isBlank())
            throw new IllegalArgumentException("Missing --" + key);
        return value;
    }

    private static double proximity(String side)
    {
        if ("front".equalsIgnoreCase(side))
            return 1.0;
        if ("side".equalsIgnoreCase(side))
            return 1.1;
        if ("behind".equalsIgnoreCase(side))
            return 1.2;
        throw new IllegalArgumentException("side must be front, side or behind: " + side);
    }

    /**
     * Independent evaluator of the CP barrier in Mobius {@code PlayerStatus.reduceHp}
     * ({@code PlayerStatus.java:212-222}). Only a playable attacker (player or summon) and a non-ignored
     * call spend CP; the remainder lands on HP. Entry format: {@code key:hp:cp:damage:playable:ignore}.
     */
    private static Map<String, int[]> readCpSplits(String spec)
    {
        Map<String, int[]> out = new LinkedHashMap<>();
        if (spec == null || spec.isBlank())
            return out;
        for (String entry : spec.split(";"))
        {
            String[] parts = entry.split(":");
            if (parts.length != 6)
                throw new IllegalArgumentException(
                    "cp-splits entry must be key:hp:cp:damage:playable:ignore: " + entry);
            String key = parts[0].trim();
            int hp = Integer.parseInt(parts[1].trim());
            int cp = Integer.parseInt(parts[2].trim());
            int damage = Integer.parseInt(parts[3].trim());
            boolean playable = Boolean.parseBoolean(parts[4].trim());
            boolean ignoreCp = Boolean.parseBoolean(parts[5].trim());

            int amount = Math.max(0, damage);
            int cpLoss = (playable && !ignoreCp) ? Math.min(Math.max(0, cp), amount) : 0;
            amount -= cpLoss;
            int hpLoss = Math.min(Math.max(0, hp), amount);
            out.put(key, new int[] { cpLoss, hpLoss });
        }
        return out;
    }

    private static void applyBuffs(Map<String, Double> stats, List<BuffFunc> buffs)
    {
        Map<String, Double> set = new HashMap<>();
        Map<String, Double> mul = new HashMap<>();
        Map<String, Double> add = new HashMap<>();
        for (BuffFunc f : buffs)
        {
            switch (f.op)
            {
                case "=" -> set.put(f.stat, f.value);
                case "m" -> mul.merge(f.stat, f.value, (a, b) -> a * b);
                case "a" -> add.merge(f.stat, f.value, Double::sum);
                case "s" -> add.merge(f.stat, -f.value, Double::sum);
                default -> throw new IllegalArgumentException("Unknown buff op " + f.op);
            }
        }
        for (String stat : stats.keySet())
        {
            double value = set.containsKey(stat) ? set.get(stat) : stats.get(stat);
            value *= mul.getOrDefault(stat, 1.0);
            value += add.getOrDefault(stat, 0.0);
            stats.put(stat, value);
        }
    }

    private static double gearPDefDelta(ClassData cls, Map<String, ItemData> gear)
    {
        double delta = 0;
        for (String slot : List.of("chest", "legs", "head", "feet", "gloves", "under", "cloak"))
        {
            ItemData item = gear.get(slot);
            if (item == null)
                continue;
            delta += item.pDef + enchantDefence(item.enchant) - cls.basePDef.getOrDefault(slot, 0);
        }
        return delta;
    }

    private static ClassData readClass(Path ref, int classId) throws Exception
    {
        Path dir = ref.resolve("dist/game/data/stats/chars/baseStats");
        List<Path> files;
        try (var stream = Files.list(dir))
        {
            files = stream.filter(p -> p.toString().endsWith(".xml")).sorted().toList();
        }
        for (Path file : files)
        {
            Element root = root(file);
            if (intText(child(root, "classId"), -1) != classId)
                continue;
            Element staticData = child(root, "staticData");
            ClassData c = new ClassData();
            c.classId = classId;
            c.str = intText(child(staticData, "baseSTR"), 1);
            c.dex = intText(child(staticData, "baseDEX"), 1);
            c.con = intText(child(staticData, "baseCON"), 1);
            c.men = intText(child(staticData, "baseMEN"), 1);
            c.intel = intText(child(staticData, "baseINT"), 1);
            c.wit = intText(child(staticData, "baseWIT"), 1);
            c.basePAtk = intText(child(staticData, "basePAtk"), 0);
            c.baseCritRate = intText(child(staticData, "baseCritRate"), 0);
            c.basePAtkSpd = intText(child(staticData, "basePAtkSpd"), 300);
            Element pdef = child(staticData, "basePDef");
            if (pdef != null)
            {
                for (String slot : List.of("chest", "legs", "head", "feet", "gloves", "cloak"))
                    c.basePDef.put(slot, intText(child(pdef, slot), 0));
                c.basePDef.put("under", intText(child(pdef, "underwear"), 0));
            }
            c.basePDefSum = c.basePDef.values().stream().mapToInt(Integer::intValue).sum();
            return c;
        }
        throw new IllegalArgumentException("Class " + classId + " not found under " + dir);
    }

    private static Map<String, ItemData> readGear(Path ref, String spec) throws Exception
    {
        Map<String, ItemData> gear = new HashMap<>();
        if (spec == null || spec.isBlank())
            return gear;
        for (String entry : spec.split(","))
        {
            String[] parts = entry.split(":");
            if (parts.length < 2)
                throw new IllegalArgumentException("gear entry must be slot:itemId[:enchant]: " + entry);
            String slot = parts[0].trim();
            int itemId = Integer.parseInt(parts[1].trim());
            int enchant = parts.length > 2 ? Integer.parseInt(parts[2].trim()) : 0;
            ItemData item = readItem(ref, itemId);
            item.enchant = enchant;
            gear.put(slot, item);
        }
        return gear;
    }

    private static ItemData readItem(Path ref, int itemId) throws Exception
    {
        int start = (itemId / 100) * 100;
        Path file = ref.resolve(String.format("dist/game/data/stats/items/%05d-%05d.xml", start, start + 99));
        Element root = root(file);
        Element item = firstById(root, "item", itemId);
        if (item == null)
            throw new IllegalArgumentException("Item " + itemId + " not found in " + file);
        ItemData data = new ItemData();
        data.id = itemId;
        NodeList direct = item.getChildNodes();
        for (int i = 0; i < direct.getLength(); i++)
        {
            Node node = direct.item(i);
            if (!(node instanceof Element e) || !"set".equals(e.getTagName()))
                continue;
            String name = e.getAttribute("name");
            if ("random_damage".equals(name))
                data.randomDamage = intAttr(e, "val", 0);
            if ("attack_range".equals(name))
                data.attackRange = intAttr(e, "val", 0);
            if ("weapon_type".equals(name) && "BOW".equalsIgnoreCase(e.getAttribute("val")))
                data.bow = true;
            if ("sDef".equals(name) || "shield_def".equals(name))
                data.shieldDef = intAttr(e, "val", 0);
            if ("rShld".equals(name) || "shield_defence_rate".equals(name) || "sDefRate".equals(name))
                data.shieldRate = intAttr(e, "val", 0);
            if ("shieldEvasionPenalty".equals(name))
                data.shieldEvasionPenalty = intAttr(e, "val", 0);
        }
        Element funcs = child(item, "for");
        if (funcs != null)
        {
            for (Element f : children(funcs))
            {
                // <enchant> is a per-level template (zero at +0); it is not a flat item stat.
                if ("enchant".equals(f.getTagName()))
                    continue;
                String stat = f.getAttribute("stat");
                int value = intAttr(f, "val", 0);
                if ("pAtk".equals(stat))
                    data.pAtk = value;
                if ("mAtk".equals(stat))
                    data.mAtk = value;
                if ("pAtkSpd".equals(stat))
                    data.pAtkSpd = value;
                if ("critRate".equals(stat))
                    data.critRate = value;
                if ("pDef".equals(stat))
                    data.pDef = value;
                if ("mDef".equals(stat))
                    data.mDef = value;
                if ("sDef".equals(stat))
                    data.shieldDef = value;
                if ("rShld".equals(stat))
                    data.shieldRate = value;
            }
        }
        return data;
    }

    private static NpcData readNpc(Path ref, int npcId) throws Exception
    {
        int start = (npcId / 100) * 100;
        Path file = ref.resolve(String.format("dist/game/data/stats/npcs/%05d-%05d.xml", start, start + 99));
        Element root = root(file);
        Element npc = firstById(root, "npc", npcId);
        if (npc == null)
            throw new IllegalArgumentException("NPC " + npcId + " not found in " + file);
        NpcData data = new NpcData();
        data.id = npcId;
        data.level = Integer.parseInt(npc.getAttribute("level"));
        Element stats = child(npc, "stats");
        data.str = intAttr(stats, "str", 1);
        data.dex = intAttr(stats, "dex", 1);
        data.con = intAttr(stats, "con", 1);
        data.men = intAttr(stats, "men", 1);
        data.intel = intAttr(stats, "int", 1);
        Element defence = child(stats, "defence");
        data.pDef = doubleAttr(defence, "physical", 0);
        data.mDef = doubleAttr(defence, "magical", 0);
        Element attack = child(stats, "attack");
        data.pAtk = doubleAttr(attack, "physical", 0);
        data.mAtk = doubleAttr(attack, "magical", 0);
        data.critical = intAttr(attack, "critical", 0);
        data.attackSpeed = intAttr(attack, "attackSpeed", 253);
        data.attackRange = intAttr(attack, "range", 40);
        data.randomDamage = intAttr(attack, "random", 0);
        data.accuracy = doubleAttr(attack, "accuracy", 0);
        data.evasion = doubleAttr(defence, "evasion", 0);
        data.shieldDef = intAttr(defence, "shield", 0);
        data.shieldRate = intAttr(defence, "shieldRate", 0);
        String attackType = attack != null ? attack.getAttribute("type") : "";
        data.bow = "BOW".equalsIgnoreCase(attackType);
        return data;
    }

    private static StatBonus readStatBonus(Path ref) throws Exception
    {
        Element root = root(ref.resolve("dist/game/data/stats/statBonus.xml"));
        StatBonus bonus = new StatBonus();
        for (String key : List.of("STR", "DEX", "CON", "INT", "WIT", "MEN"))
        {
            Element table = child(root, key);
            if (table == null)
                continue;
            Map<Integer, Double> values = new HashMap<>();
            for (Element stat : children(table))
                values.put(intAttr(stat, "value", 0), doubleAttr(stat, "bonus", 0));
            bonus.tables.put(key, values);
        }
        return bonus;
    }

    private static List<BuffFunc> readBuffs(Path ref, String spec) throws Exception
    {
        List<BuffFunc> out = new ArrayList<>();
        if (spec == null || spec.isBlank())
            return out;
        for (String entry : spec.split(","))
        {
            String[] parts = entry.split(":");
            if (parts.length != 2)
                throw new IllegalArgumentException("buff entry must be skillId:level: " + entry);
            int skillId = Integer.parseInt(parts[0].trim());
            int level = Integer.parseInt(parts[1].trim());
            out.addAll(readSkillFuncs(ref, skillId, level));
        }
        return out;
    }

    private static List<BuffFunc> readSkillFuncs(Path ref, int skillId, int level) throws Exception
    {
        int start = (skillId / 100) * 100;
        Path file = ref.resolve(String.format("dist/game/data/stats/skills/%05d-%05d.xml", start, start + 99));
        Element root = root(file);
        Element skill = firstById(root, "skill", skillId);
        if (skill == null)
            throw new IllegalArgumentException("Skill " + skillId + " not found in " + file);
        Map<String, List<String>> tables = new HashMap<>();
        for (Element e : children(skill))
        {
            if ("table".equals(e.getTagName()))
            {
                String name = e.getAttribute("name");
                if (name.startsWith("#"))
                    name = name.substring(1);
                tables.put(name, List.of(text(e).trim().split("\\s+")));
            }
        }
        List<BuffFunc> out = new ArrayList<>();
        Element forElement = child(skill, "for");
        if (forElement == null)
            return out;
        for (Element effect : children(forElement))
        {
            for (Element func : children(effect))
            {
                String stat = func.getAttribute("stat");
                if (stat.isBlank())
                    continue;
                double value = resolve(func.getAttribute("val"), tables, level);
                String op = switch (func.getTagName())
                {
                    case "set" -> "=";
                    case "mul" -> "m";
                    case "add" -> "a";
                    case "sub" -> "s";
                    default -> null;
                };
                if (op != null)
                    out.add(new BuffFunc(stat, op, value));
            }
        }
        return out;
    }

    /** {@code <power>} and {@code <magicLevel>} of one skill level, resolving {@code #table} references. */
    private static double[] readSkillPowerAndMagicLevel(Path ref, int skillId, int level) throws Exception
    {
        int start = (skillId / 100) * 100;
        Path file = ref.resolve(String.format("dist/game/data/stats/skills/%05d-%05d.xml", start, start + 99));
        Element root = root(file);
        Element skill = firstById(root, "skill", skillId);
        if (skill == null)
            throw new IllegalArgumentException("Skill " + skillId + " not found in " + file);
        Map<String, List<String>> tables = new HashMap<>();
        for (Element e : children(skill))
        {
            if ("table".equals(e.getTagName()))
            {
                String name = e.getAttribute("name");
                tables.put(name.startsWith("#") ? name.substring(1) : name, List.of(text(e).trim().split("\\s+")));
            }
        }
        Element power = child(skill, "power");
        Element magicLevel = child(skill, "magicLevel");
        return new double[] {
            power == null ? 0 : resolve(text(power).trim(), tables, level),
            magicLevel == null ? 0 : resolve(text(magicLevel).trim(), tables, level) };
    }

    private static double resolve(String value, Map<String, List<String>> tables, int level)
    {
        if (value.startsWith("#"))
        {
            List<String> table = tables.get(value.substring(1));
            if (table == null)
                throw new IllegalArgumentException("Unknown skill table " + value);
            int index = Math.max(0, Math.min(level - 1, table.size() - 1));
            return Double.parseDouble(table.get(index));
        }
        return Double.parseDouble(value);
    }

    private static String toJson(String scenario, String referenceLabel, Map<String, Double> metrics, boolean cpBarrier, boolean matrix)
    {
        StringBuilder b = new StringBuilder();
        b.append("{\n");
        b.append("  \"scenario\": ").append(q(scenario)).append(",\n");
        b.append("  \"oracle\": {\n");
        b.append("    \"kind\": \"mobius-formula-java\",\n");
        b.append("    \"reference\": ").append(q(referenceLabel)).append(",\n");
        b.append("    \"driver\": \"tools/Fineage.MobiusDiff/java/MobiusFormulaDriver.java\",\n");
        b.append("    \"formulas\": [\n");
        List<String> formulas = new ArrayList<>(List.of(
            "Formulas.calcPhysDam", "Formulas.calcCrit", "Formulas.calcHitMiss",
            "FuncPAtkMod", "FuncPDefMod", "FuncAtkAccuracy", "FuncAtkEvasion",
            "FuncAtkCritical", "FuncPAtkSpeed", "CreatureStat.getCriticalHit",
            "CreatureStat.getPAtk", "CreatureStat.getPAtkSpd"));
        if (cpBarrier)
        {
            formulas.add("PlayerStatus.reduceHp (CP barrier, PlayerStatus.java:212-222)");
            formulas.add("PlayerStatus.reduceCp (PlayerStatus.java:49-53)");
        }
        if (matrix)
        {
            formulas.add("Formulas.calcPhysDam proximity / soulshot / crit / shield");
            formulas.add("Formulas.calcShldUse");
            formulas.add("HitConditionBonusData.getConditionBonus");
            formulas.add("Creature.calculateTimeBetweenAttacks");
            formulas.add("Creature.calculateReuseTime (bow default reuse 1500)");
            formulas.add("ClassBalanceConfig PvP/PvE multipliers treated as 1.0");
        }
        if (metrics.containsKey("magicSuccessRate"))
        {
            formulas.add("Formulas.calcMagicDam (mAtk x2 sps / x4 bss, 91*sqrt(mAtk)/mDef*power, crit x3 / x2.5 PvP, half-fail, resisted, shield, random)");
            formulas.add("Formulas.calcMagicSuccess (100 - round((float) 1.3^(targetLevel - magicLevel)))");
            formulas.add("FuncMAtkMod / FuncMDefMod for NPCs (INT bonus^2 * levelMod^2, MEN bonus * levelMod)");
            formulas.add("Creature.getRandomDamageMultiplier");
            formulas.add("MagicalDamage effect ((int) damage; no hit at 0)");
        }
        for (int i = 0; i < formulas.size(); i++)
            b.append("      ").append(q(formulas.get(i))).append(i + 1 == formulas.size() ? "\n" : ",\n");
        b.append("    ],\n");
        b.append("    \"fullServerVerified\": false,\n");
        b.append("    \"blocker\": ")
            .append(q("Full Mobius GameServer startup is not reproducible in this tree: ref/mobius-ct0-interlude/dist/libs is absent (build.xml/.classpath require mariadb-java-client-2.7.5.jar) and the server needs MySQL. This driver evaluates the pure formula layer from Mobius XML/formulas; it is not a full-server run."))
            .append("\n");
        b.append("  },\n");
        b.append("  \"metrics\": {\n");
        int i = 0;
        for (var entry : metrics.entrySet())
        {
            b.append("    ").append(q(entry.getKey())).append(": ")
                .append(String.format(java.util.Locale.ROOT, "%.10f", entry.getValue()))
                .append(i + 1 == metrics.size() ? "\n" : ",\n");
            i++;
        }
        b.append("  }\n");
        b.append("}\n");
        return b.toString();
    }

    private static String q(String value)
    {
        return "\"" + value.replace("\\", "\\\\").replace("\"", "\\\"").replace("\n", "\\n") + "\"";
    }

    private static Element root(Path file) throws Exception
    {
        DocumentBuilderFactory f = DocumentBuilderFactory.newInstance();
        f.setFeature("http://apache.org/xml/features/nonvalidating/load-external-dtd", false);
        Document doc = f.newDocumentBuilder().parse(file.toFile());
        return doc.getDocumentElement();
    }

    private static Element firstById(Element root, String tag, int id)
    {
        NodeList nodes = root.getElementsByTagName(tag);
        for (int i = 0; i < nodes.getLength(); i++)
        {
            Element e = (Element) nodes.item(i);
            if (("" + id).equals(e.getAttribute("id")))
                return e;
        }
        return null;
    }

    private static Element child(Element parent, String tag)
    {
        if (parent == null)
            return null;
        for (Element e : children(parent))
        {
            if (tag.equals(e.getTagName()))
                return e;
        }
        return null;
    }

    private static List<Element> children(Element parent)
    {
        List<Element> out = new ArrayList<>();
        if (parent == null)
            return out;
        NodeList nodes = parent.getChildNodes();
        for (int i = 0; i < nodes.getLength(); i++)
        {
            if (nodes.item(i) instanceof Element e)
                out.add(e);
        }
        return out;
    }

    private static String text(Element e)
    {
        return e == null ? "" : e.getTextContent();
    }

    private static int intText(Element e, int fallback)
    {
        String value = text(e).trim();
        return value.isEmpty() ? fallback : Integer.parseInt(value);
    }

    private static int intAttr(Element e, String name, int fallback)
    {
        if (e == null || !e.hasAttribute(name))
            return fallback;
        return Integer.parseInt(e.getAttribute(name).trim());
    }

    private static double doubleAttr(Element e, String name, double fallback)
    {
        if (e == null || !e.hasAttribute(name))
            return fallback;
        return Double.parseDouble(e.getAttribute(name).trim());
    }

    private record BuffFunc(String stat, String op, double value)
    {
    }

    private static final class ClassData
    {
        int classId;
        int str;
        int dex;
        int con;
        int men;
        int intel;
        int wit;
        int basePAtk;
        int baseMAtk;
        int baseMDef = 41;
        int baseCritRate;
        int basePAtkSpd;
        int baseMAtkSpd = 333;
        final Map<String, Integer> basePDef = new LinkedHashMap<>();
        int basePDefSum;
        double[] hp = new double[0];
        double[] mp = new double[0];
        double[] cp = new double[0];
    }

    private static final class ItemData
    {
        int id;
        int pAtk;
        int mAtk;
        int pAtkSpd;
        int critRate;
        int pDef;
        int mDef;
        int randomDamage;
        int attackRange;
        int shieldDef;
        int shieldRate;
        int shieldEvasionPenalty;
        boolean bow;
        int enchant;
        String bodySlot = "";
        String weaponType = "";
        String armorType = "";
        String crystal = "";
    }

    private static final class NpcData
    {
        int id;
        int level;
        int str;
        int dex;
        int con;
        int men;
        int intel;
        double pAtk;
        double pDef;
        double mAtk;
        double mDef;
        int critical;
        double accuracy;
        double evasion;
        int attackSpeed;
        int attackRange;
        int randomDamage;
        int shieldDef;
        int shieldRate;
        boolean bow;
    }

    private static final class ActorStats
    {
        double pAtk;
        double pDef;
        double pAtkSpdRaw;
        double accuracy;
        double evasion;
        int critPermille;
        int attackRange;
        int shieldDef;
        double shieldRate;
        int randomDamage;
        int reuseDelayMs;
        double critDamageMod = 1.0;
        double critDamageAdd;
        boolean playable;
        boolean bow;
        int str, dex, con, intel, wit, men;
        double strBonus, dexBonus, conBonus, intBonus, witBonus, menBonus;
        double mAtk, mDef, mAtkSpd;
        int mCritPermille, maxHp, maxMp, maxCp;
        int level;
        boolean armed;
        boolean kindPlayer;
    }

    private static final class StatBonus
    {
        final Map<String, Map<Integer, Double>> tables = new HashMap<>();

        double bonus(String key, int value)
        {
            Map<Integer, Double> table = tables.get(key);
            if (table == null)
                throw new IllegalArgumentException("statBonus.xml has no " + key + " table");
            Double result = table.get(value);
            if (result == null)
                throw new IllegalArgumentException("statBonus.xml " + key + " has no value " + value);
            return result;
        }
    }

    /**
     * Prefers Mobius XML under {@code ref/dist/game/data/stats}. When that tree is absent
     * (common in this checkout: only the Java sources are cloned), falls back to Fineage's
     * Mobius-transcribed JSON catalogs under {@code data/}. Formulas stay in this driver;
     * the catalogs supply the same Interlude numbers the XML would have provided.
     */
    private static final class DataSource
    {
        final Path ref;
        final Path data;
        // Each catalog is read from the Mobius XML when that part of the tree is checked out, else from the Fineage JSON.
        boolean xml;
        boolean classXml;
        boolean itemXml;
        boolean bonusXml;
        boolean npcXml;
        boolean skillXml;
        String classesJson;
        String itemsJson;
        String npcsJson;
        String passivesJson;
        String skillCombatJson;
        StatBonus bonus;

        static DataSource open(Path ref, Path data) throws Exception
        {
            DataSource src = new DataSource(ref, data);
            Path stats = ref.resolve("dist/game/data/stats");
            src.classXml = Files.isDirectory(stats.resolve("chars/baseStats"));
            src.itemXml = Files.isDirectory(stats.resolve("items"));
            src.bonusXml = Files.isRegularFile(stats.resolve("statBonus.xml"));
            src.npcXml = Files.isDirectory(stats.resolve("npcs"));
            src.skillXml = Files.isDirectory(stats.resolve("skills"));
            src.xml = src.classXml && src.itemXml && src.bonusXml && src.npcXml && src.skillXml;
            boolean anyXml = src.classXml || src.itemXml || src.bonusXml || src.npcXml || src.skillXml;
            if (!anyXml && (data == null || !Files.isDirectory(data)))
                throw new IllegalArgumentException(
                    "Mobius XML tree dist/game/data/stats is missing under " + ref
                        + " and --data (Fineage transcribed catalogs) was not provided.");
            return src;
        }

        DataSource(Path ref, Path data)
        {
            this.ref = ref;
            this.data = data;
        }

        ClassData readClass(int classId) throws Exception
        {
            if (classXml)
                return MobiusFormulaDriver.readClass(ref, classId);
            return readClassJson(classId);
        }

        Map<String, ItemData> readGear(String spec) throws Exception
        {
            Map<String, ItemData> gear = new HashMap<>();
            if (spec == null || spec.isBlank())
                return gear;
            for (String entry : spec.split(","))
            {
                if (entry.isBlank())
                    continue;
                String[] parts = entry.split(":");
                if (parts.length < 2)
                    throw new IllegalArgumentException("gear entry must be slot:itemId[:enchant]: " + entry);
                String slot = parts[0].trim();
                int itemId = Integer.parseInt(parts[1].trim());
                int enchant = parts.length > 2 ? Integer.parseInt(parts[2].trim()) : 0;
                ItemData item = readItem(itemId);
                item.enchant = enchant;
                gear.put(slot, item);
            }
            return gear;
        }

        ItemData readItem(int itemId) throws Exception
        {
            if (itemXml)
                return MobiusFormulaDriver.readItem(ref, itemId);
            return readItemJson(itemId);
        }

        /** Returns {power, magicLevel} for a skill level: Mobius skill XML when present, else skill-combat.json. */
        double[] readMagicSkill(int skillId, int level) throws Exception
        {
            if (skillXml)
                return MobiusFormulaDriver.readSkillPowerAndMagicLevel(ref, skillId, level);
            if (skillCombatJson == null)
                skillCombatJson = Files.readString(data.resolve("skill-combat.json"), StandardCharsets.UTF_8);
            String obj = jsonObjectInArray(skillCombatJson, "skills", "skillId", skillId);
            if (obj == null)
                throw new IllegalArgumentException("Skill " + skillId + " not found in skill-combat.json");
            return new double[] { jsonArrayAt(obj, "power", level), jsonArrayAt(obj, "magicLevel", level) };
        }

        NpcData readNpc(int npcId) throws Exception
        {
            if (npcXml)
                return MobiusFormulaDriver.readNpc(ref, npcId);
            return readNpcJson(npcId);
        }

        StatBonus readStatBonus() throws Exception
        {
            if (bonus != null)
                return bonus;
            bonus = bonusXml ? MobiusFormulaDriver.readStatBonus(ref) : readStatBonusJson();
            return bonus;
        }

        List<BuffFunc> readBuffs(String spec) throws Exception
        {
            if (spec == null || spec.isBlank())
                return List.of();
            if (!skillXml)
                throw new IllegalArgumentException(
                    "Buffs require Mobius skill XML under dist/game/data/stats/skills; it is absent in this tree.");
            return MobiusFormulaDriver.readBuffs(ref, spec);
        }

        List<BuffFunc> readPassives(String spec, Map<String, ItemData> gear) throws Exception
        {
            List<BuffFunc> out = new ArrayList<>();
            if (spec == null || spec.isBlank())
                return out;
            if (passivesJson == null)
                passivesJson = Files.readString(data.resolve("skill-passives.json"), StandardCharsets.UTF_8);
            for (String entry : spec.split(","))
            {
                if (entry.isBlank())
                    continue;
                String[] parts = entry.split(":");
                if (parts.length != 2)
                    throw new IllegalArgumentException("passive entry must be skillId:level: " + entry);
                int skillId = Integer.parseInt(parts[0].trim());
                int level = Integer.parseInt(parts[1].trim());
                String obj = jsonObjectInArray(passivesJson, "skills", "skillId", skillId);
                if (obj == null)
                    throw new IllegalArgumentException("Passive " + skillId + " not found in skill-passives.json");
                out.addAll(parsePassiveFuncs(obj, level, gear));
            }
            return out;
        }

        ClassData readClassJson(int classId) throws Exception
        {
            if (classesJson == null)
                classesJson = Files.readString(data.resolve("classes.json"), StandardCharsets.UTF_8);
            String obj = jsonObjectInArray(classesJson, "classes", "classId", classId);
            if (obj == null)
                throw new IllegalArgumentException("Class " + classId + " not found in classes.json");
            ClassData c = new ClassData();
            c.classId = classId;
            c.str = jsonInt(obj, "str", 1);
            c.dex = jsonInt(obj, "dex", 1);
            c.con = jsonInt(obj, "con", 1);
            c.men = jsonInt(obj, "men", 1);
            c.intel = jsonInt(obj, "int", 1);
            c.wit = jsonInt(obj, "wit", 1);
            c.basePAtk = jsonInt(obj, "pAtk", 0);
            c.baseMAtk = jsonInt(obj, "mAtk", 0);
            c.baseMDef = jsonInt(obj, "mDef", 41);
            c.baseCritRate = jsonInt(obj, "crit", 0);
            c.basePAtkSpd = jsonInt(obj, "pAtkSpd", 300);
            c.baseMAtkSpd = jsonInt(obj, "mAtkSpd", 333);
            c.hp = toArray(jsonNumberArray(obj, "hp"));
            c.mp = toArray(jsonNumberArray(obj, "mp"));
            c.cp = toArray(jsonNumberArray(obj, "cp"));
            int pDefSum = jsonInt(obj, "pDef", 80);
            boolean mystic = pDefSum < 80;
            c.basePDef.put("chest", mystic ? 15 : 31);
            c.basePDef.put("legs", mystic ? 8 : 18);
            c.basePDef.put("head", 12);
            c.basePDef.put("feet", 7);
            c.basePDef.put("gloves", 8);
            c.basePDef.put("under", 3);
            c.basePDef.put("cloak", 1);
            c.basePDefSum = c.basePDef.values().stream().mapToInt(Integer::intValue).sum();
            return c;
        }

        ItemData readItemJson(int itemId) throws Exception
        {
            if (itemsJson == null)
                itemsJson = Files.readString(data.resolve("items.json"), StandardCharsets.UTF_8);
            String obj = jsonObjectInArray(itemsJson, "items", "id", itemId);
            if (obj == null)
                throw new IllegalArgumentException("Item " + itemId + " not found in items.json");
            ItemData dataItem = new ItemData();
            dataItem.id = itemId;
            dataItem.pAtk = jsonInt(obj, "pAtk", 0);
            dataItem.mAtk = jsonInt(obj, "mAtk", 0);
            dataItem.pAtkSpd = jsonInt(obj, "pAtkSpd", 0);
            dataItem.critRate = jsonInt(obj, "critRate", 0);
            dataItem.pDef = jsonInt(obj, "pDef", 0);
            dataItem.mDef = jsonInt(obj, "mDef", 0);
            dataItem.randomDamage = jsonInt(obj, "randomDamage", 0);
            dataItem.attackRange = jsonInt(obj, "attackRange", 0);
            dataItem.shieldDef = jsonInt(obj, "shieldDef", 0);
            dataItem.shieldRate = jsonInt(obj, "shieldRate", 0);
            dataItem.shieldEvasionPenalty = jsonInt(obj, "shieldEvasionPenalty", 0);
            dataItem.bow = "bow".equalsIgnoreCase(jsonString(obj, "weaponType", ""));
            dataItem.weaponType = jsonString(obj, "weaponType", "");
            dataItem.bodySlot = jsonString(obj, "bodySlot", "");
            dataItem.armorType = jsonString(obj, "armorType", "");
            dataItem.crystal = jsonString(obj, "crystal", "");
            return dataItem;
        }

        NpcData readNpcJson(int npcId) throws Exception
        {
            if (npcsJson == null)
                npcsJson = Files.readString(data.resolve("npcs.json"), StandardCharsets.UTF_8);
            String obj = jsonObjectInArray(npcsJson, "npcs", "npcId", npcId);
            if (obj == null)
                throw new IllegalArgumentException("NPC " + npcId + " not found in npcs.json");
            NpcData dataNpc = new NpcData();
            dataNpc.id = npcId;
            dataNpc.level = jsonInt(obj, "level", 1);
            dataNpc.str = jsonInt(obj, "str", 40);
            dataNpc.dex = jsonInt(obj, "dex", 30);
            dataNpc.con = jsonInt(obj, "con", 43);
            dataNpc.men = jsonInt(obj, "men", 10);
            dataNpc.intel = jsonInt(obj, "int", 21);
            dataNpc.mAtk = jsonDouble(obj, "mAtk", 0);
            dataNpc.pAtk = jsonDouble(obj, "pAtk", 0);
            dataNpc.pDef = jsonDouble(obj, "pDef", 0);
            dataNpc.mDef = jsonDouble(obj, "mDef", 0);
            dataNpc.critical = jsonInt(obj, "critical", 0);
            dataNpc.accuracy = jsonDouble(obj, "accuracy", 0);
            dataNpc.evasion = jsonDouble(obj, "evasion", 0);
            dataNpc.attackSpeed = jsonInt(obj, "attackSpeed", 253);
            dataNpc.attackRange = jsonInt(obj, "attackRange", 40);
            dataNpc.randomDamage = jsonInt(obj, "randomDamage", 0);
            dataNpc.shieldDef = jsonInt(obj, "shieldDef", 0);
            dataNpc.shieldRate = jsonInt(obj, "shieldRate", 0);
            dataNpc.bow = "BOW".equalsIgnoreCase(jsonString(obj, "attackType", ""));
            return dataNpc;
        }

        StatBonus readStatBonusJson() throws Exception
        {
            String json = Files.readString(data.resolve("stat-bonuses.json"), StandardCharsets.UTF_8);
            StatBonus bonus = new StatBonus();
            for (String key : List.of("STR", "DEX", "CON", "INT", "WIT", "MEN"))
            {
                String field = key.toLowerCase(java.util.Locale.ROOT);
                List<Double> values = jsonNumberArray(json, field);
                Map<Integer, Double> table = new HashMap<>();
                for (int i = 0; i < values.size(); i++)
                    table.put(i, values.get(i));
                bonus.tables.put(key, table);
            }
            return bonus;
        }
    }

    /** Element {@code level - 1} (clamped) of a numeric array field inside one JSON object; 0 when absent. */
    private static double jsonArrayAt(String obj, String field, int level)
    {
        int key = obj.indexOf("\"" + field + "\"");
        if (key < 0)
            return 0;
        int open = obj.indexOf('[', key);
        int close = obj.indexOf(']', open);
        if (open < 0 || close < 0)
            return 0;
        String body = obj.substring(open + 1, close).trim();
        if (body.isEmpty())
            return 0;
        String[] parts = body.split(",");
        int index = Math.max(0, Math.min(level - 1, parts.length - 1));
        return Double.parseDouble(parts[index].trim());
    }

    private static String jsonObjectInArray(String json, String arrayName, String idField, int id)
    {
        String needle = "\"" + arrayName + "\"";
        int arrayKey = json.indexOf(needle);
        if (arrayKey < 0)
            return null;
        int bracket = json.indexOf('[', arrayKey);
        if (bracket < 0)
            return null;
        String token = "\"" + idField + "\": " + id;
        String tokenTight = "\"" + idField + "\":" + id;
        int from = bracket;
        while (true)
        {
            int a = json.indexOf(token, from);
            int b = json.indexOf(tokenTight, from);
            int hit;
            if (a < 0) hit = b;
            else if (b < 0) hit = a;
            else hit = Math.min(a, b);
            if (hit < 0)
                return null;
            // Must be a whole number (not 20001 when looking for 2000, etc.).
            int after = hit + (json.startsWith(token, hit) ? token.length() : tokenTight.length());
            if (after < json.length() && Character.isDigit(json.charAt(after)))
            {
                from = after;
                continue;
            }
            int start = json.lastIndexOf('{', hit);
            if (start < 0)
                return null;
            int depth = 0;
            for (int i = start; i < json.length(); i++)
            {
                char ch = json.charAt(i);
                if (ch == '{') depth++;
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0)
                        return json.substring(start, i + 1);
                }
            }
            return null;
        }
    }

    private static int jsonInt(String obj, String field, int fallback)
    {
        Double n = jsonNumber(obj, field);
        return n == null ? fallback : n.intValue();
    }

    private static double jsonDouble(String obj, String field, double fallback)
    {
        Double n = jsonNumber(obj, field);
        return n == null ? fallback : n;
    }

    private static Double jsonNumber(String obj, String field)
    {
        String[] patterns = { "\"" + field + "\": ", "\"" + field + "\":" };
        for (String pat : patterns)
        {
            int i = obj.indexOf(pat);
            if (i < 0)
                continue;
            int s = i + pat.length();
            int e = s;
            if (e < obj.length() && obj.charAt(e) == '-') e++;
            boolean dot = false;
            while (e < obj.length())
            {
                char ch = obj.charAt(e);
                if (ch >= '0' && ch <= '9') { e++; continue; }
                if (ch == '.' && !dot) { dot = true; e++; continue; }
                break;
            }
            if (e == s || (e == s + 1 && obj.charAt(s) == '-'))
                continue;
            return Double.parseDouble(obj.substring(s, e));
        }
        return null;
    }

    private static String jsonString(String obj, String field, String fallback)
    {
        String[] patterns = { "\"" + field + "\": \"", "\"" + field + "\":\"" };
        for (String pat : patterns)
        {
            int i = obj.indexOf(pat);
            if (i < 0)
                continue;
            int s = i + pat.length();
            int e = obj.indexOf('"', s);
            if (e < 0)
                continue;
            return obj.substring(s, e);
        }
        return fallback;
    }

    private static List<Double> jsonNumberArray(String json, String field)
    {
        String needle = "\"" + field + "\"";
        int key = json.indexOf(needle);
        if (key < 0)
            throw new IllegalArgumentException("stat-bonuses.json missing " + field);
        int bracket = json.indexOf('[', key);
        int end = json.indexOf(']', bracket);
        String body = json.substring(bracket + 1, end);
        List<Double> out = new ArrayList<>();
        for (String part : body.split(","))
        {
            String t = part.trim();
            if (!t.isEmpty())
                out.add(Double.parseDouble(t));
        }
        return out;
    }


    private static void addStatMatrix(Map<String, Double> metrics, ActorStats a)
    {
        metrics.put("str", (double) a.str);
        metrics.put("dex", (double) a.dex);
        metrics.put("con", (double) a.con);
        metrics.put("int", (double) a.intel);
        metrics.put("wit", (double) a.wit);
        metrics.put("men", (double) a.men);
        metrics.put("strBonus", a.strBonus);
        metrics.put("dexBonus", a.dexBonus);
        metrics.put("conBonus", a.conBonus);
        metrics.put("intBonus", a.intBonus);
        metrics.put("witBonus", a.witBonus);
        metrics.put("menBonus", a.menBonus);
        metrics.put("mAtk", a.mAtk);
        metrics.put("mDef", a.mDef);
        metrics.put("mAtkSpd", a.mAtkSpd);
        metrics.put("mCritPermille", (double) a.mCritPermille);
        metrics.put("maxHp", (double) a.maxHp);
        metrics.put("maxMp", (double) a.maxMp);
        metrics.put("maxCp", (double) a.maxCp);
    }

    private static int enchantDefence(int enchantLevel)
    {
        if (enchantLevel <= 0)
            return 0;
        int over = enchantLevel > 3 ? enchantLevel - 3 : 0;
        int enchant = enchantLevel > 3 ? 3 : enchantLevel;
        return enchant + 3 * over;
    }

    private static int enchantPhysical(ItemData weapon)
    {
        if (weapon.enchant <= 0)
            return 0;
        int over = weapon.enchant > 3 ? weapon.enchant - 3 : 0;
        int enchant = weapon.enchant > 3 ? 3 : weapon.enchant;
        boolean twoHand = "lrhand".equalsIgnoreCase(weapon.bodySlot);
        boolean bow = weapon.bow;
        String c = weapon.crystal == null ? "" : weapon.crystal.toLowerCase();
        if ("s".equals(c))
        {
            if (twoHand)
                return bow ? 10 * enchant + 20 * over : 6 * enchant + 12 * over;
            return 5 * enchant + 10 * over;
        }
        if ("a".equals(c))
        {
            if (twoHand)
                return bow ? 8 * enchant + 16 * over : 5 * enchant + 10 * over;
            return 4 * enchant + 8 * over;
        }
        if ("b".equals(c) || "c".equals(c))
        {
            if (twoHand)
                return bow ? 6 * enchant + 12 * over : 4 * enchant + 8 * over;
            return 3 * enchant + 6 * over;
        }
        return bow ? 4 * enchant + 8 * over : 2 * enchant + 4 * over;
    }

    private static int enchantMagic(ItemData weapon)
    {
        if (weapon.enchant <= 0)
            return 0;
        int over = weapon.enchant > 3 ? weapon.enchant - 3 : 0;
        int enchant = weapon.enchant > 3 ? 3 : weapon.enchant;
        String c = weapon.crystal == null ? "" : weapon.crystal.toLowerCase();
        if ("s".equals(c))
            return 4 * enchant + 8 * over;
        if ("a".equals(c) || "b".equals(c) || "c".equals(c))
            return 3 * enchant + 6 * over;
        return 2 * enchant + 4 * over;
    }

    private static List<BuffFunc> parsePassiveFuncs(String obj, int level, Map<String, ItemData> gear)
    {
        List<BuffFunc> out = new ArrayList<>();
        int key = obj.indexOf("\"effects\"");
        if (key < 0)
            return out;
        int start = obj.indexOf('[', key);
        if (start < 0)
            return out;
        for (String effect : jsonObjectList(obj.substring(start)))
        {
            String stat = jsonString(effect, "stat", "");
            String opName = jsonString(effect, "op", "");
            String op = switch (opName)
            {
                case "mul" -> "m";
                case "add" -> "a";
                case "sub" -> "s";
                case "set" -> "=";
                default -> "";
            };
            if (stat.isEmpty() || op.isEmpty())
                continue;
            if (!usingMatches(effect, gear))
                continue;
            List<Double> values = jsonNumberArray(effect, "values");
            if (values.isEmpty())
                continue;
            int idx = Math.max(0, Math.min(level, values.size()) - 1);
            out.add(new BuffFunc(stat, op, values.get(idx)));
        }
        return out;
    }

    private static boolean usingMatches(String effect, Map<String, ItemData> gear)
    {
        List<String> kinds = jsonStringArray(effect, "using");
        List<String> slots = jsonStringArray(effect, "usingSlot");
        if (kinds.isEmpty() && slots.isEmpty())
            return true;
        ItemData weapon = gear.get("rhand");
        if (weapon == null)
            weapon = gear.get("lrhand");
        ItemData chest = gear.get("chest");
        ItemData legs = gear.get("legs");
        ItemData lhand = gear.get("lhand");
        for (String slot : slots)
        {
            if (weapon != null && slot.equalsIgnoreCase(weapon.bodySlot))
                return true;
        }
        if (!slots.isEmpty() && kinds.isEmpty())
            return false;
        for (String kind : kinds)
        {
            String k = kind.toUpperCase();
            if (k.equals("HEAVY") || k.equals("LIGHT") || k.equals("MAGIC"))
            {
                String want = k.equals("HEAVY") ? "heavy" : k.equals("LIGHT") ? "light" : "magic";
                boolean fullBody = chest != null && "fullarmor".equalsIgnoreCase(chest.bodySlot);
                if (chest != null && want.equalsIgnoreCase(nullToEmpty(chest.armorType))
                    && (fullBody || (legs != null && want.equalsIgnoreCase(nullToEmpty(legs.armorType)))))
                    return true;
            }
            else if (k.equals("SHIELD"))
            {
                if (lhand != null && lhand.shieldDef > 0)
                    return true;
            }
            else if (weapon != null && weaponKindMatches(k, nullToEmpty(weapon.weaponType)))
                return true;
        }
        return kinds.isEmpty();
    }

    private static boolean weaponKindMatches(String kind, String weaponType)
    {
        String wt = weaponType.toLowerCase();
        return switch (kind)
        {
            case "SWORD" -> wt.equals("sword");
            case "BLUNT" -> wt.equals("blunt");
            case "DAGGER", "DUALDAGGER" -> wt.equals("dagger");
            case "BOW" -> wt.equals("bow");
            case "POLE" -> wt.equals("pole");
            case "DUAL" -> wt.equals("dual");
            case "FIST", "DUALFIST" -> wt.equals("fist");
            case "FISHINGROD" -> wt.equals("fishingrod");
            case "ETC" -> wt.equals("etc");
            default -> false;
        };
    }

    private static String nullToEmpty(String value)
    {
        return value == null ? "" : value;
    }

    private static List<String> jsonObjectList(String array)
    {
        List<String> out = new ArrayList<>();
        int i = 0;
        while (i < array.length() && array.charAt(i) != '[')
            i++;
        if (i >= array.length())
            return out;
        i++;
        int depth = 0;
        int start = -1;
        for (; i < array.length(); i++)
        {
            char c = array.charAt(i);
            if (c == '{')
            {
                if (depth == 0)
                    start = i;
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0 && start >= 0)
                {
                    out.add(array.substring(start, i + 1));
                    start = -1;
                }
            }
            else if (c == ']' && depth == 0)
                break;
        }
        return out;
    }

    private static List<String> jsonStringArray(String json, String field)
    {
        List<String> out = new ArrayList<>();
        String needle = "\"" + field + "\"";
        int key = json.indexOf(needle);
        if (key < 0)
            return out;
        int bracket = json.indexOf('[', key);
        int end = json.indexOf(']', bracket);
        if (bracket < 0 || end < 0)
            return out;
        String body = json.substring(bracket + 1, end);
        for (String part : body.split(","))
        {
            String t = part.trim();
            if (t.startsWith("\"") && t.endsWith("\"") && t.length() >= 2)
                out.add(t.substring(1, t.length() - 1));
        }
        return out;
    }

    private static double[] toArray(List<Double> list)
    {
        double[] a = new double[list.size()];
        for (int i = 0; i < list.size(); i++)
            a[i] = list.get(i);
        return a;
    }

}
