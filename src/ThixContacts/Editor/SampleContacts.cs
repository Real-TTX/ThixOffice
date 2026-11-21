using MatCloud.Contract.Contacts;
using ThixContacts.Core;

namespace ThixContacts.Editor;

/// <summary>
/// The sample contacts of the snapshots and the self test: a small firm's address book – colleagues, customers, a supplier, family – with groups,
/// categories, favourites and one duplicate (Anna Berger twice), so every view has something to show. No real person.
/// </summary>
public static class SampleContacts
{
    public static void Fill(ContactsSession session)
    {
        static ContactValue Mail(string value, string label = ContactLabels.Work) => new(label, value);
        static ContactValue Phone(string value, string label = ContactLabels.Mobile) => new(label, value);
        var ids = new Dictionary<string, string>();
        void Add(string key, ContactData data) => ids[key] = session.Save(null, data);

        Add("anna", new ContactData
        {
            GivenName = "Anna", FamilyName = "Berger", Company = "Nordlicht Design GmbH", Department = "Projektleitung", JobTitle = "Projektleiterin",
            Emails = [Mail("anna.berger@nordlicht.example"), Mail("anna@berger-privat.example", ContactLabels.Home)],
            Phones = [Phone("+49 171 2345678"), Phone("+49 89 1234 560", ContactLabels.Work)],
            Addresses = [new ContactAddress { Label = ContactLabels.Work, Street = "Leopoldstraße 41", PostalCode = "80802", City = "München", Country = "Deutschland" }],
            Urls = [new ContactValue(ContactLabels.Work, "https://nordlicht.example")],
            Birthday = "1986-03-14", Categories = ["Team", "Wichtig"], IsFavorite = true,
            Notes = "Ansprechpartnerin für das Projekt Nordlicht. Montags im Büro, sonst lieber per E-Mail.",
        });
        Add("anna2", new ContactData { GivenName = "Anna", FamilyName = "Berger", Emails = [Mail("anna.berger@nordlicht.example")], Phones = [Phone("0171 2345678")], JobTitle = "Projektleitung" });
        Add("bert", new ContactData
        {
            GivenName = "Bert", FamilyName = "Hoffmann", Company = "Hoffmann Druck KG", JobTitle = "Inhaber",
            Emails = [Mail("b.hoffmann@hoffmann-druck.example")], Phones = [Phone("+49 221 987650", ContactLabels.Work), Phone("+49 221 987651", ContactLabels.WorkFax)],
            Addresses = [new ContactAddress { Label = ContactLabels.Work, Street = "Am Markt 3", PostalCode = "50667", City = "Köln", Country = "Deutschland" }],
            Categories = ["Lieferant"],
        });
        Add("clara", new ContactData { GivenName = "Clara", FamilyName = "Neumann", Company = "Stadtwerke Lindenau", JobTitle = "Einkauf", Emails = [Mail("c.neumann@stadtwerke-lindenau.example")], Phones = [Phone("+49 341 555 0120", ContactLabels.Work)], Categories = ["Kunde"], IsFavorite = true });
        Add("david", new ContactData { GivenName = "David", FamilyName = "Okafor", Company = "Nordlicht Design GmbH", JobTitle = "Entwickler", Emails = [Mail("david.okafor@nordlicht.example")], Phones = [Phone("+49 160 9876543")], Categories = ["Team"], Birthday = "--07-22" });
        Add("elif", new ContactData { GivenName = "Elif", FamilyName = "Yılmaz", Company = "Nordlicht Design GmbH", JobTitle = "Designerin", Emails = [Mail("elif.yilmaz@nordlicht.example")], Categories = ["Team"] });
        Add("frank", new ContactData { Prefix = "Dr.", GivenName = "Frank", FamilyName = "Zimmermann", Company = "Praxis am Park", Phones = [Phone("+49 89 4455667", ContactLabels.Work)], Emails = [Mail("praxis@zimmermann-am-park.example")] });
        Add("greta", new ContactData { GivenName = "Greta", FamilyName = "Berger", Nickname = "Oma", Phones = [Phone("+49 8151 12345", ContactLabels.Home)], Categories = ["Familie"], Birthday = "1949-11-02", Addresses = [new ContactAddress { Label = ContactLabels.Home, Street = "Seeweg 7", PostalCode = "82319", City = "Starnberg" }] });
        Add("hanna", new ContactData { GivenName = "Hanna", FamilyName = "Schulz", Company = "Bäckerei Schulz", Emails = [Mail("info@baeckerei-schulz.example")], Phones = [Phone("+49 89 333222", ContactLabels.Main)], Categories = ["Kunde"] });
        Add("ivan", new ContactData { GivenName = "Ivan", FamilyName = "Petrović", Company = "Nordlicht Design GmbH", JobTitle = "Werkstudent", Emails = [Mail("ivan.petrovic@nordlicht.example")], Categories = ["Team"] });
        Add("julia", new ContactData { GivenName = "Julia", FamilyName = "Wagner", Emails = [Mail("julia.wagner@example.com", ContactLabels.Home)], Phones = [Phone("+49 152 1112223")], Categories = ["Familie"], IsFavorite = true });
        Add("stadtwerke", new ContactData { Company = "Stadtwerke Lindenau", Phones = [Phone("+49 341 555 0", ContactLabels.Main)], Urls = [new ContactValue(ContactLabels.Work, "https://stadtwerke-lindenau.example")], Categories = ["Kunde"] });

        var team = session.CreateGroup("Projekt Nordlicht");
        session.SetMembers(team, [ids["anna"], ids["david"], ids["elif"], ids["ivan"], ids["clara"]], add: true);
        var family = session.CreateGroup("Familie");
        session.SetMembers(family, [ids["greta"], ids["julia"]], add: true);
    }
}
