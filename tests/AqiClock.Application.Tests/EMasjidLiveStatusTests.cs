using AqiClock.App.Services;

namespace AqiClock.Application.Tests;

public sealed class EMasjidLiveStatusTests
{
    [Theory]
    [InlineData("<small class='text-danger status-label'>Currently Offline</small>", false)]
    [InlineData("<small class=\"text-light status-label\">Currently Offline</small>", false)]
    [InlineData("<audio controls id=\"track\"><source src='unused'></audio>", true)]
    [InlineData("<AUDIO id = 'track' controls></AUDIO>", true)]
    [InlineData("<html>Service unavailable</html>", null)]
    [InlineData("<script>var track = document.getElementById('track');</script>", null)]
    [InlineData("<audio id='track'></audio><small class='status-label'>Currently Offline</small>", false)]
    public void OfficialPlayerDistinguishesLiveOfflineAndUnknown(string html, bool? expected)
        => Assert.Equal(expected, EMasjidLiveStatus.ParseAvailability(html));
}
