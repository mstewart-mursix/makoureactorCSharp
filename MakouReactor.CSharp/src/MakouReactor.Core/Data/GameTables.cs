namespace MakouReactor.Core.Data;

/// <summary>A field music track: its index, internal AKAO name and display title.</summary>
/// <param name="Id">Track index (position in the game's music list).</param>
/// <param name="Name">Internal short name, e.g. <c>"tifa"</c>.</param>
/// <param name="Title">Display title, e.g. <c>"Tifa's Theme"</c>.</param>
public sealed record MusicTrack(int Id, string Name, string Title);

/// <summary>A name lookup result: an exact match, or the closest suggestions when there is none.</summary>
/// <typeparam name="T">Entry type.</typeparam>
public sealed record NameLookup<T>(T? Match, IReadOnlyList<T> Suggestions) where T : class
{
    /// <summary>True when an exact (case/punctuation-insensitive) match was found.</summary>
    public bool Found => Match != null;
}

/// <summary>
/// Static game name tables shared by the scene-plan mapper and validator. Values are copied
/// from the C++ reference (<c>Data::musicList</c> / <c>Data::musicList2</c> in <c>src/Data.cpp</c>).
/// Item names are not embedded: the C++ app reads them from the game's kernel data at runtime.
/// </summary>
public static class GameTables
{
    /// <summary>All 100 music tracks, indexed by <see cref="MusicTrack.Id"/>.</summary>
    public static IReadOnlyList<MusicTrack> MusicTracks { get; } =
    [
        new(0, "none", "none"),
        new(1, "nothing", "nothing"),
        new(2, "oa", "Opening - Bombing Mission"),
        new(3, "ob", "Bombing Mission"),
        new(4, "dun2", "Chasing The Black-Caped Man"),
        new(5, "guitar2", "On The Other Side Of The Mountain"),
        new(6, "fanfare", "Fanfare (intro)"),
        new(7, "makoro", "Mako Reactor"),
        new(8, "bat", "Fighting"),
        new(9, "fiddle", "Fiddle De Chocobo"),
        new(10, "kurai", "Anxious Heart"),
        new(11, "chu", "Still More Fighting"),
        new(12, "ketc", "Cait Sith's Theme"),
        new(13, "earis", "Aerith's Theme"),
        new(14, "ta", "Final Fantasy VII Main Theme"),
        new(15, "tb", "Final Fantasy VII Main Theme B"),
        new(16, "sato", "sato"),
        new(17, "parade", "Rufus' Welcoming Ceremony"),
        new(18, "comical", "comical"),
        new(19, "yume", "Who Are You"),
        new(20, "mati", "Ahead on Our Way"),
        new(21, "sido", "It's Difficult to Stand on Both Feet, Isn't It"),
        new(22, "siera", "If You Open Your Heart"),
        new(23, "walz", "Waltz de Chocobo"),
        new(24, "corneo", "Don of the Slums"),
        new(25, "horror", "Trail Of Blood"),
        new(26, "canyon", "Cosmo Canyon"),
        new(27, "red", "Red XIII's Theme"),
        new(28, "seto", "Great Warrior"),
        new(29, "ayasi", "Lurking in the Darkness"),
        new(30, "sinra", "Shinra Company"),
        new(31, "sinraslo", "Infiltrating Shinra Tower"),
        new(32, "dokubo", "Underneath the Rotting Pizza"),
        new(33, "bokujo", "Farm Boy"),
        new(34, "tm", "On That Day, Five Years Ago"),
        new(35, "tifa", "Tifa's Theme"),
        new(36, "costa", "Costa Del Sol"),
        new(37, "rocket", "Oppressed People"),
        new(38, "earislo", "Flowers Blooming in the Church"),
        new(39, "chase", "Crazy Motorcycle"),
        new(40, "rukei", "Sandy Badlands"),
        new(41, "cephiros", "Those Chosen by the Planet"),
        new(42, "barret", "Barret's Theme"),
        new(43, "corel", "Mining Town"),
        new(44, "boo", "Life Stream"),
        new(45, "elec", "Electric de Chocobo"),
        new(46, "rhythm", "Turk's Theme"),
        new(47, "fan2", "Fanfare"),
        new(48, "hiku", "Highwind Takes To The Skies"),
        new(49, "cannon", "The Makou Cannon Is Fired ~ Shinra Explodes A"),
        new(50, "date", "Interrupted By Fireworks"),
        new(51, "cintro", "Those Chosen By The Planet (Intro)"),
        new(52, "cinco", "Cinco de Chocobo"),
        new(53, "chu2", "J-E-N-O-V-A"),
        new(54, "yufi", "Descendant Of Shinobi"),
        new(55, "aseri", "Hurry!"),
        new(56, "gold1", "Gold Saucer"),
        new(57, "mura1", "Parochial Town"),
        new(58, "yado", "Good Night, Until Tomorrow"),
        new(59, "over2", "Continue"),
        new(60, "crwin", "A Great Success"),
        new(61, "crlost", "Tango Of Tears"),
        new(62, "odds", "Racing Chocobo ~ Place Your Bets"),
        new(63, "geki", "Debut"),
        new(64, "junon", "Off The Edge Of Despair"),
        new(65, "tender", "Holding My Thoughts In My Heart"),
        new(66, "wind", "wind"),
        new(67, "vincent", "The Nightmare's Beginning"),
        new(68, "bee", "Honeybee Manor"),
        new(69, "jukai", "Forest Temple"),
        new(70, "sadbar", "Mark of the Traitor"),
        new(71, "aseri2", "Hurry Faster!"),
        new(72, "kita", "The Great Northern Cave"),
        new(73, "sid2", "Cid's Theme"),
        new(74, "sadsid", "Sending a Dream Into the Universe"),
        new(75, "iseki", "You Can Hear the Cry of the Planet"),
        new(76, "hen", "Who Am I"),
        new(77, "utai", "Utai"),
        new(78, "snow", "Buried in the Snow"),
        new(79, "yufi2", "Stolen Materia"),
        new(80, "mekyu", "Reunion"),
        new(81, "condor", "Fortress Of The Condor"),
        new(82, "lb2", "One-Winged Angel"),
        new(83, "gun", "Shinra Army Wages A Full-Scale Attack"),
        new(84, "weapon", "Weapon Raid"),
        new(85, "pj", "Jenova Absolute"),
        new(86, "sea", "A Secret, Sleeping in the Deep Sea"),
        new(87, "ld", "Judgment Day"),
        new(88, "lb1", "The Birth of God"),
        new(89, "sensui", "sensui"),
        new(90, "ro", "The Countdown Begins"),
        new(91, "jyro", "Steal The Tiny Bronco!"),
        new(92, "nointro", "Those Chosen By The Planet (no intro)"),
        new(93, "riku", "The Makou Cannon Is Fired ~ Shinra Explodes B"),
        new(94, "si", "The Makou Cannon Is Fired ~ Shinra Explodes C"),
        new(95, "mogu", "Highwind Takes to the Skies"),
        new(96, "pre", "The Prelude"),
        new(97, "fin", "World Crisis"),
        new(98, "heart", "heart"),
        new(99, "roll", "Staff Roll"),
    ];

    /// <summary>
    /// Find a music track by internal name, display title, or numeric id. Matching ignores case,
    /// spaces and punctuation ("Tifa's theme" = "tifas-theme"). Without an exact match, returns up
    /// to <paramref name="maxSuggestions"/> closest names by edit distance.
    /// </summary>
    public static NameLookup<MusicTrack> FindMusic(string query, int maxSuggestions = 3)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (int.TryParse(query.Trim(), out var id) && id >= 0 && id < MusicTracks.Count)
            return new(MusicTracks[id], Array.Empty<MusicTrack>());

        var key = Normalize(query);
        var match = MusicTracks.FirstOrDefault(t => Normalize(t.Name) == key || Normalize(t.Title) == key);
        if (match != null || key.Length == 0)
            return new(match, Array.Empty<MusicTrack>());

        var suggestions = MusicTracks
            .Select(t => (Track: t, Distance: Math.Min(EditDistance(key, Normalize(t.Name)), EditDistance(key, Normalize(t.Title)))))
            .Where(x => x.Distance <= Math.Max(2, key.Length / 3))
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Track.Id)
            .Take(maxSuggestions)
            .Select(x => x.Track)
            .ToList();
        return new(null, suggestions);
    }

    /// <summary>Lower-case letters and digits only.</summary>
    internal static string Normalize(string s) =>
        new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>Levenshtein distance.</summary>
    internal static int EditDistance(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
