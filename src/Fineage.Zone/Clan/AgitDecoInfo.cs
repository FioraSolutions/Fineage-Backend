using Fineage.Accounts;
using Fineage.Protocol;

namespace Fineage.Zone.Clan;

/// <summary>
/// Mobius <c>AgitDecoInfo.writeImpl</c> decoration bytes (after the hall id).
/// L2J_Mobius_CT_0_Interlude/java/org/l2jmobius/gameserver/network/serverpackets/AgitDecoInfo.java:44-186.
/// </summary>
public static class AgitDecoInfo
{
    public static byte[] Encode(ClanHallSave hall, int grade)
    {
        int Level(int type) => hall.Functions.FirstOrDefault(f => f.Type == type)?.Level ?? 0;
        return
        [
            DecoHp(grade, Level(HallFunctionProtocol.FuncRestoreHp)),
            ..MpDeco(grade, Level(HallFunctionProtocol.FuncRestoreMp)),
            DecoExp(grade, Level(HallFunctionProtocol.FuncRestoreExp)),
            DecoTeleport(Level(HallFunctionProtocol.FuncTeleport)),
            0,
            DecoCurtains(Level(HallFunctionProtocol.FuncDecoCurtains)),
            DecoItemCreate(grade, Level(HallFunctionProtocol.FuncItemCreate)),
            ..SupportDeco(grade, Level(HallFunctionProtocol.FuncSupport)),
            DecoFront(Level(HallFunctionProtocol.FuncDecoFront)),
            DecoItemCreate(grade, Level(HallFunctionProtocol.FuncItemCreate)),
        ];
    }

    static byte DecoHp(int grade, int lvl)
    {
        if (lvl <= 0)
            return 0;
        if ((grade == 0 && lvl < 220) || (grade == 1 && lvl < 160) || (grade == 2 && lvl < 260) || (grade == 3 && lvl < 300))
            return 1;
        return 2;
    }

    static byte[] MpDeco(int grade, int lvl)
    {
        if (lvl <= 0)
            return [0, 0];
        if (((grade == 0 || grade == 1) && lvl < 25) || (grade == 2 && lvl < 30) || (grade == 3 && lvl < 40))
            return [1, 1];
        return [2, 2];
    }

    static byte DecoExp(int grade, int lvl)
    {
        if (lvl <= 0)
            return 0;
        if ((grade == 0 && lvl < 25) || (grade == 1 && lvl < 30) || (grade == 2 && lvl < 40) || (grade == 3 && lvl < 50))
            return 1;
        return 2;
    }

    static byte DecoTeleport(int lvl)
    {
        if (lvl <= 0)
            return 0;
        return lvl < 2 ? (byte)1 : (byte)2;
    }

    static byte DecoCurtains(int lvl)
    {
        if (lvl <= 0)
            return 0;
        return lvl <= 1 ? (byte)1 : (byte)2;
    }

    static byte DecoItemCreate(int grade, int lvl)
    {
        if (lvl <= 0)
            return 0;
        if ((grade == 0 && lvl < 2) || lvl < 3)
            return 1;
        return 2;
    }

    static byte[] SupportDeco(int grade, int lvl)
    {
        if (lvl <= 0)
            return [0, 0];
        if ((grade == 0 && lvl < 2) || (grade == 1 && lvl < 4) || (grade == 2 && lvl < 5) || (grade == 3 && lvl < 8))
            return [1, 1];
        return [2, 2];
    }

    static byte DecoFront(int lvl)
    {
        if (lvl <= 0)
            return 0;
        return lvl <= 1 ? (byte)1 : (byte)2;
    }
}