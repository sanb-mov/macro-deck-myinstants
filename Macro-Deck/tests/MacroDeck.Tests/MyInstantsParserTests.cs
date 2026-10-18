using SuchByte.MacroDeck.Soundboard;

namespace MacroDeck.Tests;

[TestFixture]
public class MyInstantsParserTests
{
    private const string Html = """
        <div class="instant">
        <button class="small-button" onclick="play('/media/sounds/vine-boom.mp3', 'loader-81126', 'vine-boom-sound-70972')" title="Play"></button>
        <a href="/en/instant/vine-boom-sound-70972/" class="instant-link link-secondary">VINE BOOM &amp; SOUND</a>
        </div>
        <div class="instant">
        <button onclick="play('/media/sounds/faaah.mp3', 'loader-502601', 'faaah-63455')"></button>
        <a href="/en/instant/faaah-63455/" class="instant-link link-secondary">FAAAH</a>
        </div>
        <button onclick="play('/media/sounds/faaah.mp3', 'loader-502601', 'faaah-63455')"></button>
        <a href="/en/instant/faaah-63455/" class="instant-link link-secondary">FAAAH</a>
        """;

    [Test]
    public void Parse_ExtractsSoundsAndDeduplicates()
    {
        var sounds = MyInstantsService.Parse(Html);

        Assert.That(sounds, Has.Count.EqualTo(2));
        Assert.That(sounds[0].Id, Is.EqualTo("vine-boom-sound-70972"));
        Assert.That(sounds[0].Name, Is.EqualTo("VINE BOOM & SOUND"));
        Assert.That(sounds[0].Url, Is.EqualTo("https://www.myinstants.com/media/sounds/vine-boom.mp3"));
    }
}
