using Travelio.Domain;

namespace Travelio.Application;

/// <summary>Editorial inspirations available offline; current places come from ITravelDataProvider.</summary>
public sealed class DemoDestinationCatalog : IDestinationCatalog
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Destination> _downloaded = new();
    public IReadOnlyList<Destination> All { get; } =
    [
        new("lisbon", "Lizbona", "Portugalia", "PT", "Europa",
            "Pastelowe kamienice, ocean na horyzoncie i małe przyjemności w rytmie slow.",
            "Europe/Lisbon", 320, new(38.7223, -9.1393), "lisbon.jpg",
            [TravelStyle.Culture, TravelStyle.Food, TravelStyle.Beach], [3,4,5,6,9,10],
            [
                A("alfama", "Spacer po Alfamie", "Zgub się w najstarszych uliczkach miasta.", TravelStyle.Culture,38.711,-9.129,90,0),
                A("castle", "Castelo de São Jorge", "Panorama miasta z murów zamku.",TravelStyle.Culture,38.7139,-9.1335,90,65),
                A("miradouro", "Miradouro da Graça", "Zatrzymaj się na kawę z widokiem.",TravelStyle.Nature,38.716,-9.131,45,0),
                A("praca", "Praça do Comércio", "Szeroki plac otwarty na rzekę Tag.",TravelStyle.Culture,38.7078,-9.1365,45,0),
                A("chiado", "Kawiarnie w Chiado", "Mała przerwa na kawę i pastel de nata.",TravelStyle.Food,38.7106,-9.1423,60,40),
                A("market", "Time Out Market", "Smaki Portugalii pod jednym dachem.",TravelStyle.Food,38.707,-9.1457,90,100,10,22),
                A("belem", "Wieża Belém", "Nadbrzeżny spacer śladami odkrywców.",TravelStyle.Culture,38.6916,-9.216,60,40,10,18),
                A("jeronimos", "Klasztor Hieronimitów", "Misterny detal i spokojne krużganki.",TravelStyle.Culture,38.6979,-9.2067,90,75,10,18),
                A("pasteis", "Pastéis de Belém", "Czas na słodką przerwę.",TravelStyle.Food,38.6975,-9.2033,40,30),
                A("lx", "LX Factory", "Pracownie, księgarnie i industrialny klimat.",TravelStyle.Culture,38.7032,-9.178,90,0,10,20),
                A("ocean", "Oceanarium", "Podwodny świat w Parque das Nações.",TravelStyle.Nature,38.7636,-9.0939,120,110,10,19)
            ]),
        new("rome", "Rzym", "Włochy", "IT", "Europa",
            "Wielka historia, espresso przy barze i uliczki, które prowadzą do kolejnej zachwycającej piazzy.",
            "Europe/Rome", 420, new(41.9028,12.4964), "rome.jpg",
            [TravelStyle.Culture,TravelStyle.Food],[3,4,5,9,10,11],
            [
                A("colosseum","Koloseum","Symbol antycznego Rzymu.",TravelStyle.Culture,41.8902,12.4922,120,100),
                A("forum","Forum Romanum","Spacer przez historię.",TravelStyle.Culture,41.8925,12.4853,90,60),
                A("trevi","Fontanna di Trevi","Barok w sercu miasta.",TravelStyle.Culture,41.9009,12.4833,45,0),
                A("pantheon","Panteon","Niezwykła kopuła i światło.",TravelStyle.Culture,41.8986,12.4769,60,25),
                A("navona","Piazza Navona","Fontanny i włoskie dolce vita.",TravelStyle.Culture,41.8992,12.4731,45,0),
                A("trastevere","Trastevere","Wieczorny spacer i trattorie.",TravelStyle.Food,41.8896,12.4706,90,90),
                A("borghese","Villa Borghese","Zielona przerwa w mieście.",TravelStyle.Nature,41.9142,12.4923,120,0),
                A("vatican","Muzea Watykańskie","Sztuka w monumentalnym otoczeniu.",TravelStyle.Culture,41.9065,12.4536,180,130)
            ]),
        new("bali", "Bali", "Indonezja", "ID", "Azja",
            "Poranki wśród pól ryżowych, tropikalna zieleń i słońce chowające się za oceanem.",
            "Asia/Makassar", 240, new(-8.5069,115.2625), "bali.jpg",
            [TravelStyle.Nature,TravelStyle.Beach,TravelStyle.Adventure],[4,5,6,7,8,9],
            [
                A("ubud","Pałac w Ubud","Tradycyjna architektura Bali.",TravelStyle.Culture,-8.5069,115.2625,60,20),
                A("saraswati","Świątynia Saraswati","Ogrody i kwiaty lotosu.",TravelStyle.Culture,-8.506,115.261,60,20),
                A("ubudmarket","Targ w Ubud","Rzemiosło i lokalne smaki.",TravelStyle.Food,-8.5077,115.263,90,40),
                A("campuhan","Campuhan Ridge Walk","Spacer na zielonych wzgórzach.",TravelStyle.Nature,-8.499,115.254,120,0),
                A("monkeyforest","Sacred Monkey Forest","Zielony las w centrum Ubud.",TravelStyle.Nature,-8.5186,115.2588,90,30),
                A("agung","Muzeum Agung Rai","Sztuka i balijskie ogrody.",TravelStyle.Culture,-8.5238,115.2653,90,40),
                A("yoga","Spokojny poranek w Ubud","Czas na jogę i odpoczynek.",TravelStyle.Nature,-8.518,115.265,75,50)
            ]),
        new("kyoto", "Kioto", "Japonia", "JP", "Azja",
            "Ogrody zen, herbata matcha i tysiące czerwonych bram prowadzących w głąb lasu.",
            "Asia/Tokyo", 480, new(35.0116,135.7681), "kyoto.jpg",
            [TravelStyle.Culture,TravelStyle.Food,TravelStyle.Nature],[3,4,5,10,11],
            [
                A("nijo","Zamek Nijō","Rezydencja szogunów i ogrody.",TravelStyle.Culture,35.0142,135.7482,120,55),
                A("palace","Pałac Cesarski","Park w sercu Kioto.",TravelStyle.Culture,35.0254,135.7621,120,0),
                A("nishiki","Nishiki Market","Smaki dawnej stolicy.",TravelStyle.Food,35.005,135.7649,90,80,10,18),
                A("gion","Gion","Drewniane domy i spokojne zaułki.",TravelStyle.Culture,35.003,135.778,90,0),
                A("yasaka","Świątynia Yasaka","Sanktuarium przy parku Maruyama.",TravelStyle.Culture,35.0037,135.7785,60,0),
                A("kiyomizu","Kiyomizu-dera","Widok na miasto z drewnianego tarasu.",TravelStyle.Culture,34.9948,135.785,90,25),
                A("fushimi","Fushimi Inari","Szlak pośród czerwonych torii.",TravelStyle.Nature,34.9671,135.7727,150,0)
            ]),
        new("barcelona", "Barcelona", "Hiszpania", "ES", "Europa",
            "Architektura, która porusza wyobraźnię, tapas i popołudnia nad Morzem Śródziemnym.",
            "Europe/Madrid", 390, new(41.3874,2.1686), "barcelona.jpg",
            [TravelStyle.Culture,TravelStyle.Beach,TravelStyle.Food],[4,5,6,9,10],
            [
                A("sagrada","Sagrada Família","Światło i wyobraźnia Gaudíego.",TravelStyle.Culture,41.4036,2.1744,120,130),
                A("batllo","Casa Batlló","Falujące kształty modernizmu.",TravelStyle.Culture,41.3917,2.1649,90,150),
                A("gothic","Dzielnica Gotycka","Kamienne uliczki starego miasta.",TravelStyle.Culture,41.3833,2.1761,90,0),
                A("boqueria","La Boqueria","Kolory i smaki targu.",TravelStyle.Food,41.3816,2.1719,60,65),
                A("ciutadella","Parc de la Ciutadella","Zielona przerwa i fontanna.",TravelStyle.Nature,41.3881,2.186,90,0),
                A("beach","Barceloneta","Spacer nad morzem.",TravelStyle.Beach,41.3784,2.1925,120,0),
                A("guell","Park Güell","Mozaiki i panorama miasta.",TravelStyle.Culture,41.4145,2.1527,120,80)
            ]),
        new("reykjavik", "Reykjavík", "Islandia", "IS", "Europa",
            "Surowe krajobrazy, ciepłe baseny i północne światło. Przygoda zaczyna się tuż za rogiem.",
            "Atlantic/Reykjavik", 650, new(64.1466,-21.9426), "iceland.jpg",
            [TravelStyle.Nature,TravelStyle.Adventure],[2,3,6,7,8,9],
            [
                A("hallgrim","Hallgrímskirkja","Charakterystyczna sylwetka miasta.",TravelStyle.Culture,64.1417,-21.9267,60,45),
                A("harpa","Harpa","Szklana architektura nad oceanem.",TravelStyle.Culture,64.1504,-21.9324,60,0),
                A("sunvoyager","Sun Voyager","Rzeźba z widokiem na zatokę.",TravelStyle.Nature,64.1476,-21.9223,45,0),
                A("tjornin","Jezioro Tjörnin","Spokojny spacer w centrum.",TravelStyle.Nature,64.144,-21.942,75,0),
                A("perlan","Perlan","Natura Islandii pod kopułą.",TravelStyle.Nature,64.1293,-21.9191,150,170),
                A("harbour","Stary port","Kawiarnie i morski klimat.",TravelStyle.Food,64.153,-21.949,90,100),
                A("national","Muzeum Narodowe","Historia wyspy.",TravelStyle.Culture,64.142,-21.949,120,85)
            ])
    ];

    public void Register(Destination destination) => _downloaded[destination.Id] = destination;
    public Destination Get(string id) => _downloaded.TryGetValue(id, out var found) ? found : All.FirstOrDefault(x => x.Id == id)
        ?? throw new DomainException("Nieznany kierunek podróży.");

    private static Attraction A(string id, string name, string description, TravelStyle style,
        double lat, double lon, int duration, decimal cost, int open = 9, int close = 18) =>
        new(id, name, description, style, new(lat, lon), duration, null, new(9, 0), new(18, 0));
}

