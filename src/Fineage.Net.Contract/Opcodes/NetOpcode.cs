namespace Fineage.Net.Contract
{
    /// <summary>Namespaced opcodes. High byte ≈ domain; low byte ≈ message.</summary>
    public static class NetOpcode
    {
        // Session 0x01xx
        public const ushort Hello = 0x0101;
        public const ushort HelloAck = 0x0102;
        public const ushort Reject = 0x01FF;

        // Lobby 0x02xx (login port)
        public const ushort Auth = 0x0201;
        public const ushort AuthResult = 0x0202;
        public const ushort ListCharacters = 0x0203;
        public const ushort CharacterRoster = 0x0204;
        public const ushort CreateCharacter = 0x0205;
        public const ushort CreateResult = 0x0206;
        public const ushort SelectCharacter = 0x0207;
        public const ushort Handoff = 0x0208;

        // World 0x03xx (zone port)
        public const ushort ZoneAuth = 0x0301;
        public const ushort ZoneAuthResult = 0x0302;
        public const ushort EnterWorld = 0x0303;
        public const ushort EntitySpawn = 0x0304;
        public const ushort PlayerStats = 0x0305;
        public const ushort MoveInput = 0x0306;
        public const ushort StopMove = 0x0307;
        public const ushort MoveState = 0x0308;
        public const ushort PositionCorrection = 0x0309;
        public const ushort ItemList = 0x030A;
        public const ushort SkillList = 0x030B;
        public const ushort EntityDespawn = 0x030C;
        public const ushort SelectTarget = 0x030D;
        public const ushort AttackRequest = 0x030E;
        public const ushort TargetSelected = 0x030F;
        public const ushort AttackState = 0x0310;
        public const ushort StatusUpdate = 0x0311;
        public const ushort EntityDie = 0x0312;
        public const ushort DropItem = 0x0313;
        public const ushort PickupRequest = 0x0314;
        public const ushort InventoryUpdate = 0x0315;
        public const ushort DropItemRequest = 0x0316;
        public const ushort UseItem = 0x0317;
        public const ushort UnequipItem = 0x0318;
    }
}
