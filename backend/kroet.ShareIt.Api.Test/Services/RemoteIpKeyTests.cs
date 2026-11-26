using System.Net;
using kroet.ShareIt.Api.Services;

namespace kroet.ShareIt.Api.Test.Services;

[TestClass]
public class RemoteIpKeyTests
{
    [TestMethod]
    public void RemoteIpKey_GroupsIpv6BySlash64AndNormalizesIpv4MappedAddresses()
    {
        var first = RemoteIpKey.From(IPAddress.Parse("2001:db8:1:2:1:2:3:4"));
        var second = RemoteIpKey.From(IPAddress.Parse("2001:db8:1:2:aaaa:bbbb:cccc:dddd"));

        Assert.AreEqual(first, second);
        Assert.AreEqual("2001:db8:1:2::/64", first);
        Assert.AreEqual("203.0.113.7", RemoteIpKey.From(IPAddress.Parse("203.0.113.7")));
        Assert.AreEqual("203.0.113.7", RemoteIpKey.From(IPAddress.Parse("::ffff:203.0.113.7")));
        Assert.AreEqual("unknown", RemoteIpKey.From(null));
    }
}
