namespace Catalog.Infrastructure;

// Demo seed applied by migrations. IDs are fixed so Inventory seeds can reference equipment types logically.
internal static class CatalogSeed
{
    private static readonly DateTimeOffset SeededAt = new(2026, 11, 15, 0, 0, 0, TimeSpan.Zero);

    public static readonly Guid Measurement = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    public static readonly Guid Microscopy = Guid.Parse("c0000000-0000-0000-0000-000000000002");
    public static readonly Guid Power = Guid.Parse("c0000000-0000-0000-0000-000000000003");

    public static object[] Categories =>
    [
        Category(Measurement, "Đo lường điện tử", "Máy hiện sóng, đồng hồ vạn năng, máy phát tín hiệu."),
        Category(Microscopy, "Kính hiển vi", "Kính hiển vi quang học và phụ kiện."),
        Category(Power, "Nguồn điện", "Bộ nguồn DC và thiết bị cấp nguồn cho mạch thí nghiệm.")
    ];

    public static object[] EquipmentTypes =>
    [
        Type("e0000000-0000-0000-0000-000000000001", Measurement, "Máy hiện sóng số 100MHz",
            "Dao động ký 2 kênh dùng quan sát tín hiệu điện.", "2 kênh; 100 MHz; 1 GSa/s; màn hình 7 inch",
            ["oscilloscope", "measurement", "electronics"]),
        Type("e0000000-0000-0000-0000-000000000002", Measurement, "Đồng hồ vạn năng số",
            "Đo điện áp, dòng điện, điện trở.", "True RMS; 6000 counts; CAT III 600V",
            ["multimeter", "measurement", "electronics"]),
        Type("e0000000-0000-0000-0000-000000000003", Measurement, "Máy phát tín hiệu 20MHz",
            "Tạo sóng sin/vuông/tam giác cho thí nghiệm mạch.", "2 kênh; 20 MHz; dạng sóng tùy chỉnh",
            ["function-generator", "electronics"]),
        Type("e0000000-0000-0000-0000-000000000004", Microscopy, "Kính hiển vi quang học 1000x",
            "Kính hiển vi hai mắt cho thí nghiệm sinh học.", "Độ phóng đại 40x–1000x; đèn LED",
            ["microscope", "biology"]),
        Type("e0000000-0000-0000-0000-000000000005", Power, "Bộ nguồn DC 30V/5A",
            "Nguồn DC điều chỉnh được cho mạch thí nghiệm.", "0–30 V; 0–5 A; bảo vệ quá dòng",
            ["power-supply", "electronics"])
    ];

    private static object Category(Guid id, string name, string description) => new
    {
        Id = id, Name = name, Description = description, IsActive = true, CreatedAt = SeededAt, UpdatedAt = SeededAt
    };

    private static object Type(string id, Guid categoryId, string name, string description, string specifications, string[] tags) => new
    {
        Id = Guid.Parse(id), CategoryId = categoryId, Name = name, Description = description,
        Specifications = specifications, Tags = tags, IsActive = true, CreatedAt = SeededAt, UpdatedAt = SeededAt
    };
}
