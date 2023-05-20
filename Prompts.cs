

namespace CorporAIte;

public static class Prompts
{
    public static string PredictPrompt { get; set; } = @"Voorspel de volgende chatprompt die de gebruiker wil gaan gebruiken in deze chat. Geef altijd 5 opties. Formateer je antwoord als JSON en voeg geen andere tekst toe: {""prompts"": [{""prompt"": """"}] }";


}
