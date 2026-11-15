using LabLend.Observability;
using Xunit;

namespace LabLend.Platform.UnitTests;

public sealed class CorrelationIdTests
{
    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("bad\r\nInjected: true")]
    [InlineData("contains space")] [InlineData("nonascii-đ")]
    public void Unsafe_header_is_replaced_with_safe_generated_id(string? input)
    {
        var result = CorrelationId.Resolve(input);
        Assert.True(Guid.TryParseExact(result, "N", out _));
    }

    [Fact]
    public void Header_length_is_bounded_but_valid_ids_are_preserved()
    {
        Assert.Equal(new string('a', 64), CorrelationId.Resolve(new string('a', 64)));
        Assert.NotEqual(new string('a', 65), CorrelationId.Resolve(new string('a', 65)));
        Assert.Equal("request-123_a.b", CorrelationId.Resolve("request-123_a.b"));
    }
}
