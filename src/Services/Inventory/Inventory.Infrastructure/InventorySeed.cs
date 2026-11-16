namespace Inventory.Infrastructure;

// Demo units for the fixed Catalog seed IDs (e0000000-…). The microscope has exactly ONE unit for the T07 race demo.
internal static class InventorySeed
{
    private static readonly DateTimeOffset SeededAt = new(2026, 11, 15, 0, 0, 0, TimeSpan.Zero);

    public static object[] AssetUnits =>
    [
        .. Units(1, "e0000000-0000-0000-0000-000000000001", "OSC", 3, "Phòng E101 - Tủ A"),
        .. Units(4, "e0000000-0000-0000-0000-000000000002", "DMM", 4, "Phòng E101 - Tủ B"),
        .. Units(8, "e0000000-0000-0000-0000-000000000003", "FG", 2, "Phòng E102 - Kệ 1"),
        .. Units(10, "e0000000-0000-0000-0000-000000000004", "MIC", 1, "Phòng B201 - Tủ kính"),
        .. Units(11, "e0000000-0000-0000-0000-000000000005", "PSU", 3, "Phòng E102 - Kệ 2")
    ];

    private static IEnumerable<object> Units(int firstId, string equipmentTypeId, string prefix, int count, string location) =>
        Enumerable.Range(0, count).Select(i => (object)new
        {
            Id = Guid.Parse($"a0000000-0000-0000-0000-{firstId + i:D12}"),
            EquipmentTypeId = Guid.Parse(equipmentTypeId),
            SerialNumber = $"{prefix}-{i + 1:D3}",
            Location = location,
            Status = "AVAILABLE",
            CreatedAt = SeededAt,
            UpdatedAt = SeededAt
        });
}
