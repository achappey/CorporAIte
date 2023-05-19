

namespace CorporAIte;

public static class Prompts
{
    public static string ChatName { get; set; } = "Verzin een naam voor deze chat. Gebruik maximaal 1-4 woorden. Geef in je antwoord alleen de naam en voeg geen andere tekst toe.";

    public static string PredictPrompt { get; set; } = @"Voorspel de volgende chatprompt die de gebruiker wil gaan gebruiken in deze chat. Geef 5 opties. Formateer je antwoord als JSON en voeg geen andere tekst toe: {""prompts"": [] }";


}
