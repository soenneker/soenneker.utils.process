using Soenneker.Utils.Process.Abstract;
using Soenneker.Tests.FixturedUnit;
using Xunit;

namespace Soenneker.Utils.Process.Tests;

[Collection("Collection")]
public class ProcessUtilTests : FixturedUnitTest
{
    private readonly IProcessUtil _util;

    public ProcessUtilTests(Fixture fixture, ITestOutputHelper output) : base(fixture, output)
    {
        _util = Resolve<IProcessUtil>(true);
    }

    [Fact]
    public void Default()
    {

    }
}
