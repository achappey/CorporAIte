using System.Numerics;
using System.Text;
using System.Text.Json;
using CorporAIte;
using OpenAI.Managers;
using OpenAI.ObjectModels.RequestModels;

public class AIService
{
    private readonly OpenAIService _openAIService;

    public AIService(AppConfig config)
    {
        this._openAIService = new OpenAI.Managers.OpenAIService(new OpenAI.OpenAiOptions()
        {
            ApiKey = config.OpenAI
        });
    }

    private OpenAI.ObjectModels.ResponseModels.EmbeddingCreateResponse ConvertEmbedding(byte[] embedding)
    {
        var responseJson = Encoding.UTF8.GetString(embedding);

        return JsonSerializer.Deserialize<OpenAI.ObjectModels.ResponseModels.EmbeddingCreateResponse>(responseJson);
    }

    public List<double> CompareEmbeddings(byte[] query, List<byte[]> embeddings)
    {
        var queryEmbedding = ConvertEmbedding(query);
        var queryEmbeddingVector = queryEmbedding.Data.FirstOrDefault()?.Embedding.ToArray();

        if (queryEmbeddingVector == null)
        {
            throw new ArgumentException("Invalid query embedding.");
        }

        var mergeEmbeddings = embeddings
            .SelectMany(e =>
            {
                var converted = ConvertEmbedding(e);
                return converted.Data.Select(d => CalculateCosineSimilarity(d.Embedding.ToArray(), queryEmbeddingVector));
            }).ToList();

        return mergeEmbeddings;
    }

    public async Task<byte[]> CalculateEmbeddingAsync(object input)
    {
        var embeddingRequest = new OpenAI.ObjectModels.RequestModels.EmbeddingCreateRequest()
        {
            Input = input is string ? input as string : null,
            InputAsList = input is List<string> ? input as List<string> : null,
            Model = OpenAI.ObjectModels.Models.TextEmbeddingAdaV2
        };

        var embeddingResult = await this._openAIService.Embeddings.CreateEmbedding(embeddingRequest);

        if (!embeddingResult.Successful)
        {
            throw new Exception(embeddingResult.Error.Message);
        }

        string jsonString = JsonSerializer.Serialize(embeddingResult);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonString);
        return jsonBytes;
    }

    public async Task<ChatMessage> ChatWithContextAsync(string context, float temperature,
    IEnumerable<ChatMessage> messages, 
    IEnumerable<FunctionDefinition>? functions,
    string model = null)
    {
        var messageHistory = new List<ChatMessage>(messages.Count() + 1)
    {
        new ChatMessage("system", context)
    };

        messageHistory.AddRange(messages);

        var chatCompletionRequest = new ChatCompletionCreateRequest
        {
            Model = string.IsNullOrEmpty(model) ? OpenAI.ObjectModels.Models.Gpt_3_5_Turbo_16k : model,
            Temperature = temperature,
            Messages = messageHistory,
            Functions = functions?.ToList(),
        };

        var response = await _openAIService.ChatCompletion.CreateCompletion(chatCompletionRequest).ConfigureAwait(false);

        if (!response.Successful)
        {
            switch (response.Error.Code)
            {
                case "context_length_exceeded":
                    throw new FormatException(response.Error.Message);
                default:
                    throw new Exception(response.Error.Message);
            }
        }

        return response.Choices.FirstOrDefault()?.Message;
    }


    private static double CalculateCosineSimilarity(double[] vector1, double[] vector2)
    {
        if (vector1.Length != vector2.Length)
        {
            throw new ArgumentException("Vectors must have the same length");
        }

        double dotProduct = 0;
        double norm1 = 0;
        double norm2 = 0;

        int i = 0;
        int length = Vector<double>.Count;

        // Compute dot product, norm1, and norm2 using SIMD instructions
        for (; i <= vector1.Length - length; i += length)
        {
            var vec1 = new Vector<double>(vector1, i);
            var vec2 = new Vector<double>(vector2, i);
            dotProduct += Vector.Dot(vec1, vec2);
            norm1 += Vector.Dot(vec1, vec1);
            norm2 += Vector.Dot(vec2, vec2);
        }

        // Compute the remaining elements using scalar operations
        for (; i < vector1.Length; i++)
        {
            dotProduct += vector1[i] * vector2[i];
            norm1 += vector1[i] * vector1[i];
            norm2 += vector2[i] * vector2[i];
        }

        if (norm1 == 0 || norm2 == 0)
        {
            return 0;
        }

        return dotProduct / (Math.Sqrt(norm1) * Math.Sqrt(norm2));
    }

}
