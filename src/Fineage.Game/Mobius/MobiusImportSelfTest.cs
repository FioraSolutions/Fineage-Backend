namespace Fineage.Game.Mobius;

public static class MobiusImportSelfTest
{
    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "fineage-mobius-selftest-" + Guid.NewGuid().ToString("n"));
        try
        {
            var xml = Path.Combine(root, "stats", "npcs");
            Directory.CreateDirectory(xml);
            File.WriteAllText(Path.Combine(xml, "20000-20099.xml"), """
                <?xml version="1.0" encoding="UTF-8"?>
                <list>
                  <npc id="20001" level="1" type="Monster" name="Gremlin">
                    <race>FAIRY</race>
                    <stats>
                      <vitals hp="39.74519" hpRegen="2" mp="40" mpRegen="0.9" />
                      <speed>
                        <walk ground="20" />
                        <run ground="50" />
                      </speed>
                    </stats>
                  </npc>
                </list>
                """);

            var outDir = Path.Combine(root, "out");
            var result = MobiusCatalogImporter.Run(new MobiusImportOptions
            {
                XmlRoot = root,
                OutputDirectory = outDir,
                WriteNpcs = true,
                NpcIds = [20001]
            });

            if (result.WrittenPaths.Count != 1 || !File.Exists(Path.Combine(outDir, "npcs.json")))
                return 1;

            return 0;
        }
        catch
        {
            return 1;
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }
}
