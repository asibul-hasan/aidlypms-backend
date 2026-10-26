using AidlyPms.Api.Common;
using Xunit;

namespace AidlyPms.Api.Tests;

public class DateHelperTests
{
    [Fact]
    public void ToUtcOffset_HandlesLocalKind_WithoutArgumentException()
    {
        var localDt = new DateTime(2026, 10, 26, 16, 20, 8, DateTimeKind.Local);
        var dto = DateHelper.ToUtcOffset(localDt);

        Assert.Equal(TimeSpan.Zero, dto.Offset);
        Assert.Equal(localDt.ToUniversalTime(), dto.UtcDateTime);
    }

    [Fact]
    public void ToUtcOffset_HandlesUtcKind_WithoutArgumentException()
    {
        var utcDt = new DateTime(2026, 10, 26, 16, 20, 8, DateTimeKind.Utc);
        var dto = DateHelper.ToUtcOffset(utcDt);

        Assert.Equal(TimeSpan.Zero, dto.Offset);
        Assert.Equal(utcDt, dto.UtcDateTime);
    }

    [Fact]
    public void ToUtcOffset_HandlesUnspecifiedKind_WithoutArgumentException()
    {
        var unspecDt = new DateTime(2026, 10, 26, 16, 20, 8, DateTimeKind.Unspecified);
        var dto = DateHelper.ToUtcOffset(unspecDt);

        Assert.Equal(TimeSpan.Zero, dto.Offset);
        Assert.Equal(DateTime.SpecifyKind(unspecDt, DateTimeKind.Utc), dto.UtcDateTime);
    }

    [Fact]
    public void ToUtcOffset_Nullable_HandlesNull()
    {
        DateTime? nullDt = null;
        var dto = DateHelper.ToUtcOffset(nullDt);

        Assert.Null(dto);
    }
}
