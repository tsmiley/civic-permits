namespace CivicPermits.Infrastructure.Identity;

/// <summary>
/// The stems of the passwords people guess first, written for this project from general knowledge of which words and
/// patterns are everywhere, and listed in lower case letters only. <see cref="CommonPasswordValidator"/> reads a password
/// as these stems with digits and symbols around them, so each entry stands for many thousands of the passwords a
/// guesser would try (the stem, in any capitals, with any tail of digits, with its look-alike letters swapped, repeated,
/// or joined to another stem). It is a floor, not a breach corpus.
/// </summary>
internal static class CommonPasswords
{
    /// <summary>The stems. Every one is at least four letters long and nothing but a to z.</summary>
    public static readonly string[] Stems =
    [
        // The obvious
        "password", "passwd", "passw", "passphrase", "mypassword", "yourpassword", "newpassword", "oldpassword", "letmein", "letmeinplease",
        "welcome", "secret", "topsecret", "changeme", "default", "admin", "administrator", "adminadmin", "root", "toor", "user", "guest", "login",
        "logon", "signin", "access", "master", "super", "trustno", "trustnoone", "test", "tester", "testing", "temp", "temporary", "demo", "sample",
        "example", "hello", "hellohello", "helloworld", "whatever", "nothing", "anything", "everything", "freedom", "opensesame", "sesame",
        "iamgod", "iloveyou", "iloveyoumore", "ilovemydog", "ilovemycat", "ilovemom", "ilovepizza", "loveyou", "loveme", "lover", "lovers", "lovely",
        "love", "mylove", "lovelove", "sweetheart", "darling", "honey", "baby", "babygirl", "babyboy", "angel", "angels", "princess", "prince",
        "queen", "king", "beautiful", "forever", "friend", "friends", "family", "mother", "father", "mommy", "daddy", "sister", "brother",
        // The keyboard
        "qwerty", "qwertyuiop", "qwertyui", "qwert", "qwertz", "azerty", "asdf", "asdfg", "asdfgh", "asdfghj", "asdfghjk", "asdfghjkl", "zxcv",
        "zxcvb", "zxcvbn", "zxcvbnm", "qazwsx", "qazwsxedc", "wsxedc", "edcrfv", "rfvtgb", "plmokn", "poiuyt", "lkjhgf", "mnbvcx", "qweasd",
        "qweasdzxc", "asdzxc", "zaqxsw", "qwaszx", "qazxsw", "yxcvbnm", "abcdef", "abcdefg", "abcdefgh", "abcdefghi", "abcdefghij",
        "abcdefghijk", "abcdefghijkl", "abcdefghijklm", "abcdefghijklmnop", "abcd", "abcde", "abcabc", "aaaa", "bbbb", "xxxx", "zzzz",
        // Names and things
        "monkey", "dragon", "tiger", "lion", "eagle", "falcon", "wolf", "shadow", "killer", "ninja", "pirate", "wizard", "hunter", "soldier",
        "warrior", "knight", "samurai", "cowboy", "batman", "superman", "spiderman", "ironman", "captain", "hulk", "joker", "starwars",
        "startrek", "pokemon", "naruto", "mario", "zelda", "sonic", "matrix", "gandalf", "hogwarts", "harrypotter", "minecraft", "fortnite",
        "cookie", "cookies", "chocolate", "pepper", "ginger", "butter", "banana", "cherry", "orange", "peanut", "pizza", "coffee", "whiskey",
        "buster", "buddy", "rocky", "charlie", "maggie", "jordan", "harley", "ranger", "thunder", "lightning", "phoenix", "rainbow", "flower",
        "sunshine", "sunny", "moonlight", "starlight", "midnight", "diamond", "silver", "golden", "purple", "yellow", "orange", "master",
        "mustang", "corvette", "ferrari", "porsche", "camaro", "harley", "toyota", "honda", "yamaha", "jeep", "truck", "tractor",
        // Sport and place
        "football", "baseball", "basketball", "soccer", "hockey", "tennis", "golf", "cowboys", "yankees", "lakers", "packers", "steelers",
        "patriots", "liverpool", "chelsea", "arsenal", "barcelona", "realmadrid", "manchester", "united", "england", "america", "canada",
        "texas", "kansas", "california", "florida", "newyork", "chicago", "london", "paris", "hawaii", "vegas",
        // Names, the most common
        "michael", "jennifer", "jessica", "ashley", "daniel", "andrew", "joshua", "matthew", "amanda", "nicole", "thomas", "robert", "william",
        "james", "david", "john", "johnny", "charles", "joseph", "richard", "anthony", "brandon", "jason", "justin", "taylor", "tyler", "ryan",
        "kevin", "brian", "steven", "eric", "george", "sarah", "emily", "hannah", "samantha", "elizabeth", "michelle", "stephanie", "heather",
        "melissa", "amber", "brittany", "lauren", "rachel", "jennie", "christopher", "nicholas", "alexander", "benjamin", "jonathan", "tiffany",
        "maria", "lisa", "karen", "susan", "nancy", "linda", "barbara", "patricia", "jacob", "ethan", "logan", "mason", "noah", "liam", "olivia",
        "sophia", "isabella", "madison", "abigail", "chloe", "victoria", "alex", "alexis", "austin", "dylan", "dallas", "chris", "mike", "matt",
        // Time
        "january", "february", "march", "april", "june", "july", "august", "september", "october", "november", "december", "monday", "tuesday",
        "wednesday", "thursday", "friday", "saturday", "sunday", "spring", "summer", "autumn", "winter", "christmas", "easter", "birthday",
        "holiday", "weekend", "today", "tomorrow", "yesterday",
        // Technology and brands
        "computer", "internet", "google", "gmail", "facebook", "twitter", "youtube", "instagram", "snapchat", "tiktok", "apple", "microsoft",
        "windows", "linux", "android", "iphone", "samsung", "server", "system", "network", "database", "oracle", "mysql", "sqlserver", "azure",
        "github", "cloud", "email", "online", "website", "webmaster", "hacker", "hacked", "cracker", "security", "private", "public",
        // This application
        "civicpermits", "civicpermit", "civic", "permits", "permit", "permitoffice", "bramblewick", "office", "clerk", "inspector", "applicant",
        "building", "planning", "town", "city", "council",
        // Phrases and patterns
        "correcthorsebatterystaple", "thequickbrownfox", "quickbrownfox", "mypassword", "myspace", "supersecret", "sosecret", "nosecret",
        "notsecret", "lettherightonein", "thisisapassword", "thisismypassword", "passwordpassword", "passwordisnotsecure", "iwantaccess",
        "pleaseletmein", "lovepassword", "passwordlove",
    ];

    /// <summary>The length of the longest stem, so the matcher knows how far to look.</summary>
    public static readonly int LongestStem = Stems.Max(stem => stem.Length);
}
