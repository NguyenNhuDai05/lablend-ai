using Borrowing.Application;
using Borrowing.Infrastructure;
using Xunit;

namespace LabLend.Sprint1.IntegrationTests;

public sealed class IncidentImageTests
{
    internal static byte[] Png => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==");
    [Fact]
    public void Raster_content_is_checked_instead_of_declared_MIME_or_extension()
    {
        Assert.Equal("image/png", IncidentImageValidator.Validate(Png));
        Assert.Throws<LoanFailure>(() => IncidentImageValidator.Validate("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>"u8.ToArray()));
        Assert.Throws<LoanFailure>(() => IncidentImageValidator.Validate(Png[..24]));
        var corrupted = Png; corrupted[45] ^= 1; Assert.Throws<LoanFailure>(() => IncidentImageValidator.Validate(corrupted));
        Assert.Throws<LoanFailure>(() => IncidentImageValidator.Validate(new byte[IncidentImageValidator.MaximumBytes + 1]));
    }
}
