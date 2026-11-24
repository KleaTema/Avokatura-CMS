using System.Globalization;
using System.Text.Json;
using Ash.SharedKernel.Time;

namespace Ash.DemoSeed;

/// <summary>A case the seed made, for the final table.</summary>
public sealed record SeededCase(string Label, Guid PublicId, string Code, string Office, string? Primary);

/// <summary>What the seed made: the labelled cases and every case, for the table and the tests.</summary>
public sealed record DemoSeedResult(IReadOnlyList<SeededCase> Cases, DateTime StartedUtc, DateTime FinishedUtc);

/// <summary>
/// The presentation's data, through the API only (the same handlers the UI calls, inside their own units of work):
/// the Durrës office and users, three institutions, 14 first registrations (10 TR, 4 DR) spread over Aug–Nov 2026 on
/// the controlled clock, their assignments, claims, parties, hearings, documents, decisions, an archive, a delegation, a
/// continuation to X/2, a jurist and assistants; then — at the real "now" — this week's and next week's hearings, two
/// short chats, and every bell set to 0. Nothing is written to the database directly.
/// </summary>
public sealed class DemoSeeder(DemoApi api, DemoClock clock, Func<Task> scanDocuments, TextWriter log)
{
    private const string Admin = "admin@ash.local";
    private const string Protokoll = "protokoll@ash.local";
    private const string Kryetar = "kryetar@ash.local";
    private const string Avokat1 = "avokat1@ash.local";
    private const string Avokat2 = "avokat2@ash.local";
    private const string Sekondar = "sekondar@ash.local";
    private const string Ndihmes = "ndihmes@ash.local";
    private const string Institucion = "institucion@ash.local";
    private const string JuristUser = "jurist@ash.local";
    private const string Delegim = "delegim@ash.local";
    private const string KryetarDr = "kryetar.dr@ash.local";
    private const string AvokatDr = "avokat.dr@ash.local";
    private const string SekondarDr = "sekondar.dr@ash.local";

    private const string Mfe = "Ministria e Financave dhe Ekonomisë";
    private const string Mb = "Ministria e Brendshme";
    private const string Ashk = "Agjencia Shtetërore e Kadastrës";
    private const string Bt = "Bashkia Tiranë";

    private const string FirstTirana = "Gjykata e Shkallës së Parë Tiranë";
    private const string AppealTirana = "Gjykata e Apelit Tiranë";
    private const string FirstDurres = "Gjykata e Shkallës së Parë Durrës";

    private readonly Dictionary<string, int> _courts = new();
    private readonly Dictionary<int, List<long>> _judges = new();
    private readonly Dictionary<string, Guid> _institutions = new();
    private readonly Dictionary<string, Guid> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Live> _cases = new();
    private readonly List<(DateTime At, string What, Func<Task> Run)> _steps = [];
    private DateOnly _today;

    /// <summary>A case while the seed works on it.</summary>
    private sealed class Live(string label, string office, string? primary)
    {
        public string Label { get; } = label;
        public string Office { get; } = office;
        public string? Primary { get; } = primary;
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
    }

    private sealed record Party(string Role, string Kind, string Name, string? Nipt = null);

    public async Task<DemoSeedResult> RunAsync()
    {
        var realNow = DateTime.UtcNow;
        _today = TiraneTime.DateOf(realNow);
        if (_today < new DateOnly(2026, 11, 21))
        {
            throw new InvalidOperationException("The demo timeline runs until 20.11.2026: run the seed on a later day.");
        }

        var started = clock.UtcNow;
        await SetUpOrganisationAsync();
        PlanCases();

        // every step at its own time, in time order (the clock never goes back)
        foreach (var (at, what, run) in _steps.OrderBy(s => s.At))
        {
            clock.Set(at);
            log.WriteLine($"  {TiraneTime.DateOf(at):dd.MM.yyyy} {what}");
            await run();
        }

        // the real "now": this week's and next week's hearings, the chats, every bell at 0
        clock.Set(realNow);
        await NowAsync();
        await MarkAllReadAsync();

        var result = _cases.Values
            .Select(c => new SeededCase(c.Label, c.Id, c.Code, c.Office, c.Primary))
            .ToList();
        return new DemoSeedResult(result, started, clock.UtcNow);
    }

    // ---------------------------------------------------------------- organisation

    private async Task SetUpOrganisationAsync()
    {
        clock.Set(Tirane(2026, 8, 1, 8, 0));
        log.WriteLine("Organisation (Durrës office, courts, users, institutions)");

        foreach (var c in (await api.Get(Admin, "/api/reference/courts")).EnumerateArray())
        {
            _courts[c.GetProperty("name").GetString()!] = c.GetProperty("id").GetInt32();
        }

        foreach (var j in (await api.Get(Admin, "/api/reference/judges/active")).EnumerateArray())
        {
            if (j.GetProperty("courtId").ValueKind == JsonValueKind.Number)
            {
                var court = j.GetProperty("courtId").GetInt32();
                (_judges.TryGetValue(court, out var list) ? list : _judges[court] = []).Add(j.GetProperty("id").GetInt64());
            }
        }

        // the offices: TR (seeded) handles the Tirana courts, DR (created when missing) the Durrës court
        var offices = (await api.Get(Admin, "/api/identity/admin/offices")).EnumerateArray().ToList();
        var tr = offices.Single(o => o.GetProperty("code").GetString() == "TR").GetProperty("publicId").GetGuid();
        var dr = offices.FirstOrDefault(o => o.GetProperty("code").GetString() == "DR") is { ValueKind: JsonValueKind.Object } existing
            ? existing.GetProperty("publicId").GetGuid()
            : (await api.Post(Admin, "/api/identity/admin/offices",
                new { code = "DR", name = "Zyra Vendore Durrës", officeType = "LOCAL", parentOfficeId = (Guid?)null }))
                .GetProperty("publicId").GetGuid();
        var mapped = (await api.Get(Admin, "/api/identity/admin/office-courts")).EnumerateArray()
            .Select(m => (Court: m.GetProperty("courtId").GetInt32(), Office: m.GetProperty("officePublicId").GetGuid())).ToList();
        await MapCourtsAsync(tr, mapped, [_courts[FirstTirana], _courts[AppealTirana]]);
        await MapCourtsAsync(dr, mapped, [_courts[FirstDurres]]);

        // the users of Durrës (created when missing); kryetar@ also gets STATE_LAWYER (he is primary on one case)
        await EnsureUserAsync(KryetarDr, "Arta", "Meçe", dr, ["OFFICE_HEAD"]);
        await EnsureUserAsync(AvokatDr, "Erjon", "Kapllani", dr, ["STATE_LAWYER"]);
        await EnsureUserAsync(SekondarDr, "Sidita", "Bregu", dr, ["SECONDARY_LAWYER"]);
        await api.Put(Admin, $"/api/identity/admin/offices/{dr}/head", new { userPublicId = await UserId(KryetarDr) });
        var kryetar = await AdminUserAsync(Kryetar);
        if (!kryetar.GetProperty("roles").EnumerateArray().Any(r => r.GetString() == "STATE_LAWYER"))
        {
            await api.Post(Admin, $"/api/identity/admin/users/{kryetar.GetProperty("publicId").GetGuid()}/roles", new { role = "STATE_LAWYER" });
        }

        // the institutions: the seeded ministry, plus three of the lists (created when missing)
        var known = (await api.Get(Admin, "/api/identity/institutions/active")).EnumerateArray()
            .ToDictionary(i => i.GetProperty("name").GetString()!, i => i.GetProperty("publicId").GetGuid());
        _institutions[Mfe] = known[Mfe];
        _institutions[Mb] = known.TryGetValue(Mb, out var mb) ? mb : await CreateInstitutionAsync(Mb, "MB", "MINISTRY", null);
        _institutions[Ashk] = known.TryGetValue(Ashk, out var ashk) ? ashk : await CreateInstitutionAsync(Ashk, "ASHK", "AGENCY", _institutions[Mfe]);
        _institutions[Bt] = known.TryGetValue(Bt, out var bt) ? bt : await CreateInstitutionAsync(Bt, null, "LOCAL_GOV", null);
    }

    private async Task MapCourtsAsync(Guid office, List<(int Court, Guid Office)> mapped, int[] courts)
    {
        var own = mapped.Where(m => m.Office == office).Select(m => m.Court).ToHashSet();
        if (courts.All(own.Contains))
        {
            return;
        }

        await api.Put(Admin, $"/api/identity/admin/offices/{office}/courts", new { courtIds = own.Union(courts).ToList() });
    }

    private async Task EnsureUserAsync(string email, string first, string last, Guid office, string[] roles)
    {
        if (await FindUserAsync(email) is not null)
        {
            return;
        }

        await api.Post(Admin, "/api/identity/admin/users",
            new { email, firstName = first, lastName = last, officeId = office, institutionId = (Guid?)null, roles });
    }

    private async Task<JsonElement?> FindUserAsync(string email)
    {
        var page = await api.Get(Admin, $"/api/identity/admin/users?q={Uri.EscapeDataString(email)}&limit=20");
        foreach (var u in page.GetProperty("items").EnumerateArray())
        {
            if (string.Equals(u.GetProperty("email").GetString(), email, StringComparison.OrdinalIgnoreCase))
            {
                return u;
            }
        }

        return null;
    }

    private async Task<JsonElement> AdminUserAsync(string email) =>
        await FindUserAsync(email) ?? throw new InvalidOperationException($"User {email} not found.");

    private async Task<Guid> UserId(string email)
    {
        if (!_users.TryGetValue(email, out var id))
        {
            id = (await api.Get(email, "/api/identity/me")).GetProperty("publicId").GetGuid();
            _users[email] = id;
        }

        return id;
    }

    private async Task<Guid> CreateInstitutionAsync(string name, string? shortName, string type, Guid? parent) =>
        (await api.Post(Admin, "/api/identity/admin/institutions",
            new { name, shortName, institutionType = type, parentInstitutionId = parent, nipt = (string?)null }))
        .GetProperty("publicId").GetGuid();

    // ---------------------------------------------------------------- the cases

    private void PlanCases()
    {
        var n = _today;
        // ---- Tiranë (TR)
        Case("E", "TR", FirstTirana, Avokat1, null, Tirane(2026, 10, 22, 9, 20), "FL-2026/4471", "4471/2026",
            "Shpërblim dëmi nga shpronësimi i pasurisë nr. 245/12, ZK 8270, Tiranë, për interes publik",
            [new("PADITES", "NATURAL", "Arben Krasniqi"), new("I_PADITUR", "INSTITUTION", Mfe)],
            claim: ("4850000", "ALL", null), firstHearing: Tirane(2026, 11, 6, 10, 0));
        Held("E", Tirane(2026, 11, 6, 12, 0), "Seanca përgatitore u zhvillua; paditësi paraqiti provat shkresore dhe kërkoi ekspertim.");
        Doc("E", Tirane(2026, 10, 24, 11, 0), "PADI_PROVA", "Padia e paditësit Arben Krasniqi",
            ["Paditësi kërkon shpërblimin e dëmit nga shpronësimi i pasurisë nr. 245/12 në zonën kadastrale 8270.",
             "Vlera e kërkuar sipas akt-ekspertimit të paditësit: 4 850 000 lekë."]);

        Case("F", "TR", FirstTirana, Avokat1, null, Tirane(2026, 9, 3, 10, 5), "FL-2026/3120", "3120/2026",
            "Detyrim pagese për punë të kryera sipas kontratës nr. 112/2025 për rikonstruksionin e rrugës \"Myslym Shyri\"",
            [new("PADITES", "LEGAL", "Ndërtimi Shpresa sh.p.k.", "L71512034A"), new("I_PADITUR", "INSTITUTION", Bt)],
            claim: ("12400000", "ALL", null), firstHearing: Tirane(2026, 9, 24, 9, 30));
        Assistant("F", Tirane(2026, 9, 5, 9, 0));
        Held("F", Tirane(2026, 9, 24, 11, 0), "U paraqitën kontrata dhe situacionet përfundimtare të punimeve.", next: Tirane(2026, 10, 15, 10, 0));
        Held("F", Tirane(2026, 10, 15, 12, 0), "Palët paraqitën konkluzionet përfundimtare.", next: Tirane(2026, 11, 12, 9, 0));
        Held("F", Tirane(2026, 11, 12, 10, 0), "Gjykata shpalli vendimin; pritet arsyetimi i plotë.");
        Doc("F", Tirane(2026, 9, 4, 14, 0), "PADI_PROVA", "Padia e shoqërisë Ndërtimi Shpresa sh.p.k.",
            ["Paditësi kërkon pagesën e situacionit përfundimtar të punimeve sipas kontratës nr. 112/2025.",
             "Vlera e kërkuar: 12 400 000 lekë, me kamatëvonesat ligjore."]);
        // for the live demo: the reasoned decision is uploaded, the decision is NOT recorded
        Doc("F", Tirane(2026, 11, 19, 14, 0), "VENDIM", "Vendimi nr. 5520, datë 12.11.2026",
            ["Gjykata e Shkallës së Parë Tiranë vendosi pranimin pjesërisht të padisë.",
             "Detyrimi i të paditurit Bashkia Tiranë për të paguar shumën prej 9 750 000 lekë."]);

        Case("C3", "TR", FirstTirana, Avokat1, Sekondar, Tirane(2026, 9, 15, 11, 40), "FL-2026/3388", "3388/2026",
            "Kthim në punë dhe pagesë e pagës së prapambetur pas largimit të paligjshëm nga detyra",
            [new("PADITES", "NATURAL", "Mirela Hoxha"), new("I_PADITUR", "INSTITUTION", Mb)],
            claim: ("1860000", "ALL", null), firstHearing: Tirane(2026, 10, 8, 10, 0));
        Assistant("C3", Tirane(2026, 9, 17, 9, 30));
        Held("C3", Tirane(2026, 10, 8, 11, 30), "U dëgjuan dëshmitarët e paditëses; i padituri paraqiti urdhrin e lirimit nga detyra.");
        Doc("C3", Tirane(2026, 9, 18, 10, 0), "PADI_PROVA", "Padia e paditëses Mirela Hoxha",
            ["Paditësja kërkon kthimin në vendin e punës dhe pagën e prapambetur nga data e largimit."]);
        Docx("C3", Tirane(2026, 10, 9, 15, 0), "AKT_PROCEDURAL", "Prapësimet e të paditurit",
            ["Prapësimet e Ministrisë së Brendshme ndaj padisë së paditëses Mirela Hoxha.",
             "Largimi nga detyra u bë në zbatim të ristrukturimit të miratuar me urdhrin nr. 312/2026."]);

        Case("C4", "TR", FirstTirana, Avokat1, Sekondar, Tirane(2026, 10, 28, 13, 10), "FL-2026/4610", "4610/2026",
            "Kundërshtim i aktit administrativ për vendosjen e gjobës nr. 4512, datë 14.07.2026",
            [new("PADITES", "LEGAL", "Agro Elbasani sh.p.k.", "K81425667B"), new("I_PADITUR", "INSTITUTION", Mfe)],
            claim: ("350000", "ALL", null), firstHearing: TiraneOn(n.AddDays(-5), 10, 30)); // passed, NOT updated: "Seanca kaloi"
        Jurist("C4", Tirane(2026, 10, 30, 10, 0));
        Doc("C4", Tirane(2026, 10, 29, 9, 30), "PADI_PROVA", "Padia e shoqërisë Agro Elbasani sh.p.k.",
            ["Paditësi kërkon shfuqizimin e gjobës nr. 4512, datë 14.07.2026, si të pabazuar në ligj."]);

        Case("ARCHIVED", "TR", FirstTirana, Avokat1, null, Tirane(2026, 8, 4, 9, 0), "FL-2026/2105", "2105/2026",
            "Njohje pronësie mbi truallin me sipërfaqe 600 m², ZK 3891, Tiranë",
            [new("PADITES", "NATURAL", "Gëzim Leka"), new("I_PADITUR", "INSTITUTION", Ashk)],
            claim: (null, null, "600"), firstHearing: Tirane(2026, 8, 27, 10, 0));
        Held("ARCHIVED", Tirane(2026, 8, 27, 11, 0), "U administruan certifikatat e pronësisë dhe planimetria.", next: Tirane(2026, 9, 17, 10, 0));
        Held("ARCHIVED", Tirane(2026, 9, 17, 11, 0), "Gjykata shpalli vendimin për rrëzimin e padisë.");
        Decide("ARCHIVED", Tirane(2026, 9, 30, 11, 0), "FITUAR", "3127", new DateOnly(2026, 9, 17), appealed: false, final: true,
            "Vendimi nr. 3127, datë 17.09.2026", ["Gjykata vendosi rrëzimin e padisë së paditësit Gëzim Leka.", "Vendimi ka marrë formë të prerë."]);
        Archive("ARCHIVED", Tirane(2026, 10, 6, 9, 0));

        Case("X/1", "TR", FirstTirana, Avokat2, Sekondar, Tirane(2026, 8, 11, 10, 30), "FL-2026/2241", "2241/2026",
            "Pavlefshmëri e kontratës së prokurimit publik nr. 77/2025 për furnizim me pajisje mjekësore",
            [new("PADITES", "LEGAL", "Medikal Albania sh.p.k.", "J61829005C"), new("I_PADITUR", "INSTITUTION", Mb)],
            claim: ("215000", "EUR", null), firstHearing: Tirane(2026, 9, 2, 10, 0));
        Held("X/1", Tirane(2026, 9, 2, 11, 0), "U paraqit dosja e procedurës së prokurimit.", next: Tirane(2026, 9, 23, 11, 0));
        Held("X/1", Tirane(2026, 9, 23, 12, 0), "Gjykata shpalli vendimin.");
        Decide("X/1", Tirane(2026, 10, 1, 10, 0), "HUMBUR", "4019", new DateOnly(2026, 9, 23), appealed: true, final: false,
            "Vendimi nr. 4019, datë 23.09.2026", ["Gjykata vendosi pranimin e padisë dhe pavlefshmërinë e kontratës nr. 77/2025.", "Avokatura e Shtetit ushtroi ankim."]);
        NextLevel("X/1", "X/2", Tirane(2026, 10, 14, 10, 0), Avokat2);
        Doc("X/2", Tirane(2026, 10, 16, 11, 0), "ANKIM", "Ankimi i Avokaturës së Shtetit",
            ["Ankim ndaj vendimit nr. 4019, datë 23.09.2026, të Gjykatës së Shkallës së Parë Tiranë.",
             "Kërkohet ndryshimi i vendimit dhe rrëzimi i padisë."]);

        Case("DELEGATED", "TR", FirstTirana, Avokat2, null, Tirane(2026, 9, 22, 9, 45), "FL-2026/3502", "3502/2026",
            "Detyrim pagese për shërbime transporti të kryera sipas kontratës nr. 205/2024",
            [new("PADITES", "LEGAL", "Trans Adriatik sh.p.k.", "L52107843D"), new("I_PADITUR", "INSTITUTION", Mfe)],
            claim: ("2750000", "ALL", null), firstHearing: Tirane(2026, 10, 20, 10, 0));
        Held("DELEGATED", Tirane(2026, 10, 20, 11, 0), "Seanca përgatitore; palët u njohën me provat.");
        Delegate("DELEGATED", Tirane(2026, 10, 21, 9, 30));
        DocBy("DELEGATED", Delegim, Tirane(2026, 11, 4, 10, 0), "AKT_PROCEDURAL", "Kundërpadia e Ministrisë së Financave dhe Ekonomisë",
            ["Institucioni me delegim paraqet prapësimet dhe provat e pagesave të kryera."]);

        Case("C8", "TR", FirstTirana, Avokat2, Sekondar, Tirane(2026, 8, 19, 11, 0), "FL-2026/2390", "2390/2026",
            "Shpërblim dëmi jashtëkontraktor nga aksidenti rrugor me mjet të institucionit",
            [new("PADITES", "NATURAL", "Ilir Shehu"), new("I_PADITUR", "INSTITUTION", Bt)],
            claim: ("980000", "ALL", null), firstHearing: Tirane(2026, 9, 10, 10, 0));
        Held("C8", Tirane(2026, 9, 10, 11, 0), "U dëgjua eksperti automobilistik.", next: Tirane(2026, 10, 2, 10, 0));
        Held("C8", Tirane(2026, 10, 2, 11, 0), "Gjykata shpalli vendimin.");
        Decide("C8", Tirane(2026, 10, 9, 10, 0), "PJESERISHT_FITUAR", "4388", new DateOnly(2026, 10, 2), appealed: false, final: true,
            "Vendimi nr. 4388, datë 02.10.2026", ["Gjykata pranoi pjesërisht padinë: detyrim pagese prej 420 000 lekësh.", "Vendimi ka marrë formë të prerë."]);

        Case("C9", "TR", AppealTirana, Kryetar, null, Tirane(2026, 10, 7, 10, 20), "FL-2026/3870", "3870/2026",
            "Ankim ndaj vendimit nr. 2210 të Gjykatës së Shkallës së Parë Tiranë për kthim në punë",
            [new("PADITES", "NATURAL", "Valbona Gjoka"), new("I_PADITUR", "INSTITUTION", Mb)],
            claim: ("1200000", "ALL", null), firstHearing: Tirane(2026, 10, 29, 11, 0), level: "APPEAL");
        Held("C9", Tirane(2026, 10, 29, 12, 0), "Gjykata e Apelit shqyrtoi shkaqet e ankimit.");

        Case("TR-PA-AVOKAT", "TR", FirstTirana, null, null, TiraneOn(n.AddDays(-3), 10, 0), "FL-2026/4980", "4980/2026",
            "Kundërshtim i vendimit të konfiskimit të mallit nga autoriteti doganor",
            [new("PADITES", "LEGAL", "Euro Import sh.p.k.", "K91234870E"), new("I_PADITUR", "INSTITUTION", Mfe)],
            claim: null, firstHearing: TiraneOn(n.AddDays(14), 10, 0));

        // ---- Durrës (DR)
        Case("D", "DR", FirstDurres, AvokatDr, SekondarDr, Tirane(2026, 10, 13, 9, 30), "FL-2026/D-812", "812/2026",
            "Kthim në punë dhe pagë e prapambetur e mësueses së larguar pa shkak",
            [new("PADITES", "NATURAL", "Elona Basha"), new("I_PADITUR", "INSTITUTION", Mb)],
            claim: ("1450000", "ALL", null), firstHearing: Tirane(2026, 11, 3, 10, 0));
        Held("D", Tirane(2026, 11, 3, 11, 0), "U paraqitën kontrata e punës dhe vendimi i largimit.");
        Doc("D", Tirane(2026, 10, 15, 10, 0), "PADI_PROVA", "Padia e paditëses Elona Basha",
            ["Paditësja kërkon kthimin në punë si mësuese dhe pagën e prapambetur."]);

        Case("C12", "DR", FirstDurres, AvokatDr, null, Tirane(2026, 9, 9, 14, 0), "FL-2026/D-655", "655/2026",
            "Njohje pronësie mbi apartamentin 85 m², ZK 8514, Durrës",
            [new("PADITES", "NATURAL", "Artan Dervishi"), new("I_PADITUR", "INSTITUTION", Ashk)],
            claim: ("3100000", "ALL", null), firstHearing: Tirane(2026, 10, 1, 10, 0));
        Held("C12", Tirane(2026, 10, 1, 11, 30), "U administrua kontrata e shitjes dhe vërtetimi i hipotekës.");

        Case("C13", "DR", FirstDurres, AvokatDr, null, Tirane(2026, 8, 26, 10, 0), "FL-2026/D-541", "541/2026",
            "Detyrim pagese për punimet e rikonstruksionit të shkollës 9-vjeçare në Shkozet",
            [new("PADITES", "LEGAL", "Durrës Konstruksion sh.p.k.", "L81723460F"), new("I_PADITUR", "INSTITUTION", Mfe)],
            claim: ("6700000", "ALL", null), firstHearing: Tirane(2026, 9, 18, 10, 0));
        Held("C13", Tirane(2026, 9, 18, 11, 0), "U paraqitën situacionet e punimeve.", next: Tirane(2026, 10, 30, 10, 0));
        Held("C13", Tirane(2026, 10, 30, 11, 30), "U caktua ekspertimi teknik i punimeve.");

        Case("DR-PA-AVOKAT", "DR", FirstDurres, null, null, TiraneOn(n.AddDays(-2), 11, 30), "FL-2026/D-905", "905/2026",
            "Kundërshtim i gjobës administrative të vendosur nga inspektorati shtetëror",
            [new("PADITES", "LEGAL", "Port Service sh.p.k.", "K72318954G"), new("I_PADITUR", "INSTITUTION", Mb)],
            claim: null, firstHearing: TiraneOn(n.AddDays(15), 11, 0));
    }

    /// <summary>The Protokoll registers it; the office's Kryetar assigns it the next morning; the primary enters the claim.</summary>
    private void Case(string label, string office, string court, string? primary, string? secondary, DateTime registeredAt,
        string summons, string act, string objectText, Party[] parties, (string? Amount, string? Currency, string? Area)? claim,
        DateTime firstHearing, string level = "FIRST_INSTANCE")
    {
        var live = new Live(label, office, primary);
        _cases[label] = live;
        _steps.Add((registeredAt, $"{label}: regjistrim", async () =>
        {
            var courtId = _courts[court];
            var routed = await api.Get(Protokoll, $"/api/cases/registration/offices?courtId={courtId}");
            var received = TiraneTime.DateOf(registeredAt);
            var created = await api.Post(Protokoll, "/api/cases/register", new
            {
                summonsNumber = summons,
                actNumber = act,
                actDate = received.AddDays(-6),
                receivedDate = received,
                courtLevelCode = level,
                courtId,
                // a stable choice of the court's judges (the same on every run)
                judgeId = _judges.TryGetValue(courtId, out var judges) ? judges[label.Sum(ch => ch) % judges.Count] : (long?)null,
                objectText,
                firstHearing = new { at = Iso(firstHearing), hearingTypeCode = "PERGATITORE" },
                parties = await PartiesAsync(parties),
                officePublicId = routed.GetProperty("courtOfficePublicId").GetGuid(),
            });
            live.Id = created.GetProperty("casePublicId").GetGuid();
            live.Code = created.GetProperty("caseCode").GetString()!;
        }));

        if (primary is null)
        {
            return;
        }

        var head = office == "DR" ? KryetarDr : Kryetar;
        _steps.Add((registeredAt.AddDays(1).Date.AddHours(8).AddMinutes(30), $"{label}: caktim avokati", async () =>
            await api.Post(head, $"/api/cases/{live.Id}/assignment", new
            {
                primaryUserPublicId = await UserId(primary),
                secondaryUserPublicId = secondary is null ? (Guid?)null : await UserId(secondary),
                priority = (string?)null,
                rowVersion = await RowVersion(head, live.Id),
            })));

        if (claim is { } value)
        {
            _steps.Add((registeredAt.AddDays(1).Date.AddHours(10), $"{label}: vlera e padisë", async () =>
                await api.Patch(primary, $"/api/cases/{live.Id}/claim", new
                {
                    claimAmount = value.Amount,
                    claimCurrency = value.Currency,
                    claimAreaM2 = value.Area,
                    rowVersion = await RowVersion(primary, live.Id),
                })));
        }
    }

    private async Task<object[]> PartiesAsync(Party[] parties)
    {
        await Task.CompletedTask;
        return parties.Select(p => p.Kind == "INSTITUTION"
            ? (object)new { partyRoleCode = p.Role, kind = p.Kind, institutionPublicId = _institutions[p.Name] }
            : new { partyRoleCode = p.Role, kind = p.Kind, displayName = p.Name, identifier = p.Nipt }).ToArray();
    }

    /// <summary>The case's SCHEDULED hearing is completed "U zhvillua" with a description (and optionally the next one).</summary>
    private void Held(string label, DateTime at, string description, DateTime? next = null) =>
        _steps.Add((at, $"{label}: seanca u zhvillua", async () =>
        {
            var c = _cases[label];
            var user = c.Primary ?? Protokoll;
            var hearing = (await api.Get(user, $"/api/cases/{c.Id}/hearings")).EnumerateArray()
                .Where(h => h.GetProperty("status").GetString() == "SCHEDULED")
                .OrderBy(h => h.GetProperty("scheduledAt").GetDateTime())
                .First();
            await api.Post(user, $"/api/cases/{c.Id}/hearings/{hearing.GetProperty("publicId").GetGuid()}/complete", new
            {
                outcome = "HELD",
                description,
                next = next is { } n ? new { at = Iso(n), hearingTypeCode = "GJYQESORE" } : null,
                rowVersion = hearing.GetProperty("rowVersion").GetUInt32(),
            });
        }));

    private void Assistant(string label, DateTime at) =>
        _steps.Add((at, $"{label}: ndihmës ligjor", async () =>
        {
            var c = _cases[label];
            await api.Post(c.Primary!, $"/api/cases/{c.Id}/assistants",
                new { userPublicId = await UserId(Ndihmes), rowVersion = await RowVersion(c.Primary!, c.Id) });
        }));

    private void Jurist(string label, DateTime at) =>
        _steps.Add((at, $"{label}: jurist i institucionit", async () =>
        {
            var c = _cases[label];
            await api.Post(Institucion, $"/api/cases/{c.Id}/jurist",
                new { userPublicId = await UserId(JuristUser), rowVersion = await RowVersion(Institucion, c.Id) });
        }));

    private void Delegate(string label, DateTime at) =>
        _steps.Add((at, $"{label}: delegim", async () =>
        {
            var c = _cases[label];
            await api.Post(c.Primary!, $"/api/cases/{c.Id}/delegate",
                new { institutionPublicId = _institutions[Mfe], mode = "DELEGIM", rowVersion = await RowVersion(c.Primary!, c.Id) });
        }));

    private void Doc(string label, DateTime at, string type, string title, string[] lines) =>
        DocBy(label, null, at, type, title, lines);

    private void DocBy(string label, string? user, DateTime at, string type, string title, string[] lines) =>
        _steps.Add((at, $"{label}: dokument {type}", async () =>
        {
            var c = _cases[label];
            await api.Upload(user ?? c.Primary!, c.Id, type, title, FileName(title, "pdf"), "application/pdf", DemoDocuments.Pdf(title, lines));
            await scanDocuments();
        }));

    private void Docx(string label, DateTime at, string type, string title, string[] paragraphs) =>
        _steps.Add((at, $"{label}: dokument {type} (.docx)", async () =>
        {
            var c = _cases[label];
            await api.Upload(c.Primary!, c.Id, type, title, FileName(title, "docx"),
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document", DemoDocuments.Docx(title, paragraphs));
            await scanDocuments();
        }));

    /// <summary>The reasoned decision (VENDIM PDF, scanned "Gati") is uploaded and the decision recorded: the case closes.</summary>
    private void Decide(string label, DateTime at, string result, string number, DateOnly date, bool appealed, bool final,
        string title, string[] lines) =>
        _steps.Add((at, $"{label}: vendim {result}", async () =>
        {
            var c = _cases[label];
            var upload = await api.Upload(c.Primary!, c.Id, "VENDIM", title, FileName(title, "pdf"), "application/pdf",
                DemoDocuments.Pdf(title, lines));
            await scanDocuments();
            await api.Post(c.Primary!, $"/api/cases/{c.Id}/decision", new
            {
                resultCode = result,
                decisionNumber = number,
                decisionDate = date,
                dispositive = lines[0],
                decisionReceived = true,
                decisionDocumentPublicId = upload.GetProperty("documentPublicId").GetGuid(),
                isAppealed = appealed,
                isRecursed = false,
                isFinal = final,
                cancelOpenHearings = true,
                rowVersion = await RowVersion(c.Primary!, c.Id),
            });
        }));

    private void Archive(string label, DateTime at) =>
        _steps.Add((at, $"{label}: arkivim", async () =>
        {
            var c = _cases[label];
            var header = (await api.Get(c.Primary!, $"/api/cases/{c.Id}")).GetProperty("header");
            await api.Post(c.Primary!, $"/api/dossiers/{header.GetProperty("dossierPublicId").GetGuid()}/archive",
                new { rowVersion = header.GetProperty("rowVersion").GetUInt32() });
        }));

    /// <summary>The Protokoll continues the closed level at the Tirana appeal court; the Kryetar assigns it the next day.</summary>
    private void NextLevel(string from, string label, DateTime at, string primary)
    {
        var live = new Live(label, "TR", primary);
        _cases[label] = live;
        _steps.Add((at, $"{label}: vazhdim në apel", async () =>
        {
            var previous = _cases[from];
            var court = _courts[AppealTirana];
            var received = TiraneTime.DateOf(at);
            var created = await api.Post(Protokoll, $"/api/cases/{previous.Id}/next-level", new
            {
                summonsNumber = "FL-2026/A-1190",
                actNumber = "1190/2026",
                actDate = received.AddDays(-5),
                receivedDate = received,
                courtLevelCode = "APPEAL",
                courtId = court,
                judgeId = _judges.TryGetValue(court, out var judges) ? judges[0] : (long?)null,
                // the first hearing at appeal is avokat2's hearing of this week
                firstHearing = new { at = Iso(TiraneOn(WorkingDay(_today, 2), 11, 30)), hearingTypeCode = "PERGATITORE" },
                officePublicId = (await api.Get(Protokoll, $"/api/cases/registration/offices?courtId={court}"))
                    .GetProperty("courtOfficePublicId").GetGuid(),
            });
            live.Id = created.GetProperty("casePublicId").GetGuid();
            live.Code = created.GetProperty("caseCode").GetString()!;
        }));
        _steps.Add((at.AddDays(1).Date.AddHours(8).AddMinutes(45), $"{label}: caktim avokati", async () =>
            await api.Post(Kryetar, $"/api/cases/{live.Id}/assignment", new
            {
                primaryUserPublicId = await UserId(primary),
                secondaryUserPublicId = (Guid?)null,
                priority = (string?)null,
                rowVersion = await RowVersion(Kryetar, live.Id),
            })));
    }

    // ---------------------------------------------------------------- now

    private async Task NowAsync()
    {
        log.WriteLine($"Today ({_today:dd.MM.yyyy}): hearings of this week and next week, chats");
        var thisWeek1 = WorkingDay(_today, 1);
        var thisWeek2 = WorkingDay(_today, 2);
        var nextWeek = NextMonday(_today);
        await Hearing("C3", Avokat1, TiraneOn(thisWeek1, 10, 0), "GJYQESORE", "Dëgjimi i dëshmitarit të të paditurit.");
        await Hearing("C12", AvokatDr, TiraneOn(thisWeek2, 12, 0), "GJYQESORE", "Pyetja e ekspertit të hipotekës.");
        await Hearing("E", Avokat1, TiraneOn(nextWeek, 10, 30), "GJYQESORE", "Caktimi i ekspertit për vlerësimin e pasurisë.");
        await Hearing("C9", Kryetar, TiraneOn(nextWeek.AddDays(1), 9, 30), "GJYQESORE", "Shqyrtimi i ankimit në seancë publike.");
        await Hearing("D", AvokatDr, TiraneOn(nextWeek.AddDays(2), 9, 30), "GJYQESORE", "Dëgjimi i palëve dhe i dëshmitarëve.");
        await Hearing("C4", Sekondar, TiraneOn(nextWeek.AddDays(3), 13, 0), "GJYQESORE", "Seanca e radhës pas shtyrjes.");

        await Chat("C3", [
            (Avokat1, "Kam përgatitur prapësimet për seancën e radhës; ju lutem rishikojini deri nesër."),
            (Sekondar, "I pashë. Shtova argumentin për afatin e ankimit administrativ."),
            (Ndihmes, "Urdhri i lirimit nga detyra është skanuar dhe ngarkuar te Dokumentet."),
            (Avokat1, "Faleminderit, i përfshij në konkluzione."),
        ]);
        await Chat("F", [
            (Avokat1, "Vendimi u shpall më 12 nëntor; arsyetimi është ngarkuar te Dokumentet."),
            (Ndihmes, "E kontrollova: detyrimi i caktuar është 9 750 000 lekë."),
            (Avokat1, "Mirë, po vlerësoj nëse ka shkak për ankim."),
        ]);
    }

    private async Task Hearing(string label, string user, DateTime at, string type, string description)
    {
        var c = _cases[label];
        await api.Post(user, $"/api/cases/{c.Id}/hearings", new { at = Iso(at), hearingTypeCode = type, otherTypeText = (string?)null, description });
    }

    private async Task Chat(string label, (string User, string Text)[] messages)
    {
        var c = _cases[label];
        var conversation = (await api.Post(messages[0].User, $"/api/chat/cases/{c.Id}/conversation")).GetProperty("id").GetGuid();
        foreach (var (user, text) in messages)
        {
            await api.Post(user, $"/api/chat/conversations/{conversation}/messages", new { body = text });
        }
    }

    /// <summary>Every user's bell starts at 0 in the presentation (the notifications stay, read).</summary>
    private async Task MarkAllReadAsync()
    {
        foreach (var user in new[] { Admin, Protokoll, Kryetar, Avokat1, Avokat2, Sekondar, Ndihmes, Institucion, JuristUser, Delegim,
                     KryetarDr, AvokatDr, SekondarDr, "monitor@ash.local" })
        {
            await api.Post(user, "/api/notifications/read-all");
        }
    }

    // ---------------------------------------------------------------- helpers

    private async Task<uint> RowVersion(string user, Guid caseId) =>
        (await api.Get(user, $"/api/cases/{caseId}")).GetProperty("header").GetProperty("rowVersion").GetUInt32();

    private static DateTime Tirane(int year, int month, int day, int hour, int minute) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified), TiraneTime.Zone);

    private static DateTime TiraneOn(DateOnly day, int hour, int minute) => Tirane(day.Year, day.Month, day.Day, hour, minute);

    private static string Iso(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static DateOnly WorkingDay(DateOnly from, int days)
    {
        var day = from;
        while (days > 0)
        {
            day = day.AddDays(1);
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days--;
            }
        }

        return day;
    }

    private static DateOnly NextMonday(DateOnly from)
    {
        var day = from.AddDays(1);
        while (day.DayOfWeek != DayOfWeek.Monday)
        {
            day = day.AddDays(1);
        }

        return day;
    }

    private static string FileName(string title, string extension)
    {
        var name = new string(title.Normalize(System.Text.NormalizationForm.FormD)
            .Where(ch => char.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-').ToArray());
        return $"{string.Join('-', name.Split('-', StringSplitOptions.RemoveEmptyEntries))}.{extension}";
    }
}
