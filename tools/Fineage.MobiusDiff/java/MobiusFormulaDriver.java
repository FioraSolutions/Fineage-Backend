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
        int classId = Integer.parseInt(require(opt, "class-id"));
        int level = Integer.parseInt(require(opt, "level"));
        int npcId = Integer.parseInt(require(opt, "npc"));
        String gearSpec = opt.getOrDefault("gear", "");
        String buffSpec = opt.getOrDefault("buffs", "");
        String side = opt.getOrDefault("side", "front");
        boolean soulshot = Boolean.parseBoolean(opt.getOrDefault("soulshot", "false"));
        String cpSplitSpec = opt.getOrDefault("cp-splits", "");

        ClassData cls = readClass(ref, classId);
        Map<String, ItemData> gear = readGear(ref, gearSpec);
        NpcData npc = readNpc(ref, npcId);
        StatBonus bonus = readStatBonus(ref);
        List<BuffFunc> buffs = readBuffs(ref, buffSpec);

        double prox = proximity(side);
        double strBonus = bonus.bonus("STR", cls.str);
        double dexBonus = bonus.bonus("DEX", cls.dex);
        double levelMod = (level + 89) / 100.0;

        ItemData weapon = gear.get("rhand");
        double pAtk = (weapon != null && weapon.pAtk > 0 ? weapon.pAtk : cls.basePAtk) * strBonus * levelMod;
        double pDef = (cls.basePDefSum + gearPDefDelta(cls, gear)) * levelMod;
        double pAtkSpdBase = weapon != null && weapon.pAtkSpd > 0 ? weapon.pAtkSpd : cls.basePAtkSpd;
        double pAtkSpd = pAtkSpdBase * dexBonus;
        double critBeast = weapon != null && weapon.critRate > 0 ? weapon.critRate : cls.baseCritRate;
        double crit = critBeast * dexBonus * 10.0;

        Map<String, Double> stats = new HashMap<>();
        stats.put("pAtk", pAtk);
        stats.put("pDef", pDef);
        stats.put("pAtkSpd", pAtkSpd);
        stats.put("critRate", crit);
        applyBuffs(stats, buffs);

        pAtk = stats.get("pAtk");
        pDef = stats.get("pDef");
        pAtkSpd = stats.get("pAtkSpd");
        crit = stats.get("critRate");

        int accuracy = (int) Math.round(Math.sqrt(cls.dex) * 6.0 + level);
        int evasion = (int) Math.round(Math.sqrt(cls.dex) * 6.0 + level);
        int npcEvasion = (int) Math.round(Math.sqrt(npc.dex) * 6.0 + npc.level);

        int hitChance = (int) ((80 + (2 * (accuracy - npcEvasion))) * 10);
        hitChance = Math.max(200, Math.min(980, hitChance));

        int critPermille = (int) (Math.min(crit, 500.0) + 0.5);
        int critChance = Math.min(critPermille, 500);

        double damageNoCritRaw = 76.0 * Math.max(0.0, pAtk) * prox / Math.max(1.0, npc.pDef);
        int damageNoCrit = Math.max(1, (int) damageNoCritRaw);
        double damageMean = (hitChance / 1000.0) * damageNoCritRaw * (1.0 + (critChance / 1000.0));
        double pAtkSpdRounded = Math.round(pAtkSpd);

        Map<String, Double> metrics = new LinkedHashMap<>();
        metrics.put("pAtk", pAtk);
        metrics.put("pDef", pDef);
        metrics.put("pAtkSpd", pAtkSpdRounded);
        metrics.put("accuracy", (double) accuracy);
        metrics.put("evasion", (double) evasion);
        metrics.put("critPermille", (double) critPermille);
        metrics.put("hitChancePermille", (double) hitChance);
        metrics.put("critChancePermille", (double) critChance);
        metrics.put("damageNoCrit", (double) damageNoCrit);
        metrics.put("damageMeanPerSwing", damageMean);
        for (Map.Entry<String, int[]> split : readCpSplits(cpSplitSpec).entrySet())
        {
            metrics.put("cpLoss_" + split.getKey(), (double) split.getValue()[0]);
            metrics.put("hpLoss_" + split.getKey(), (double) split.getValue()[1]);
        }

        String out = opt.get("out");
        String json = toJson(scenario, referenceLabel, metrics, !cpSplitSpec.isBlank());
        if (out == null)
        {
            System.out.println(json);
        }
        else
        {
            Files.writeString(Paths.get(out), json, StandardCharsets.UTF_8);
        }
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
            delta += item.pDef - cls.basePDef.getOrDefault(slot, 0);
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
            if (enchant != 0)
                throw new IllegalArgumentException(
                    "Oracle driver currently supports enchant 0 only (entry " + entry + ").");
            gear.put(slot, readItem(ref, itemId));
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
        Element defence = child(stats, "defence");
        data.pDef = doubleAttr(defence, "physical", 0);
        data.mDef = doubleAttr(defence, "magical", 0);
        Element attack = child(stats, "attack");
        data.pAtk = doubleAttr(attack, "physical", 0);
        data.critical = intAttr(attack, "critical", 0);
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

    private static String toJson(String scenario, String referenceLabel, Map<String, Double> metrics, boolean cpBarrier)
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
        int baseCritRate;
        int basePAtkSpd;
        final Map<String, Integer> basePDef = new LinkedHashMap<>();
        int basePDefSum;
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
    }

    private static final class NpcData
    {
        int id;
        int level;
        int str;
        int dex;
        int con;
        int men;
        double pAtk;
        double pDef;
        double mDef;
        int critical;
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
}
