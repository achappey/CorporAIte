
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Text.Json;
using OpenAI.GPT3.Managers;

public class AIService
{
    //  private readonly string _apiKey;
    private readonly OpenAIService _openAIService;

    public AIService(string apiKey)
    {
        //  this._apiKey = apiKey;
//this._openAIService = serviceProvider.GetRequiredService<OpenAIService>();
      //  this._openAIService = openAIService;
           this._openAIService = new OpenAI.GPT3.Managers.OpenAIService(new OpenAI.GPT3.OpenAiOptions()
           {
              ApiKey = apiKey
          });
    }

    private OpenAI.GPT3.ObjectModels.ResponseModels.EmbeddingCreateResponse ConvertEmbedding(byte[] embedding)
    {
        var responseJson = Encoding.UTF8.GetString(embedding);

        return JsonSerializer.Deserialize<OpenAI.GPT3.ObjectModels.ResponseModels.EmbeddingCreateResponse>(responseJson);
    }

    public async Task<List<double>> CompareEmbeddings(byte[] query, List<byte[]> embeddings)
    {
        var queryEmbedding = ConvertEmbedding(query);
        var queryEmbeddingVector = queryEmbedding.Data.FirstOrDefault().Embedding.ToArray();

        var mergeEmbeddings = embeddings.Select(a => ConvertEmbedding(a))
        .Select(p => p.Data.Select(z => CalculateCosineSimilarity(z.Embedding.ToArray(), queryEmbeddingVector)))
        .SelectMany(x => x)
        .ToList();

        return mergeEmbeddings;
    }

    public async Task<byte[]> CalculateEmbeddings(List<string> items)
    {
        var embeddingResult = await this._openAIService.Embeddings.CreateEmbedding(
            new OpenAI.GPT3.ObjectModels.RequestModels.EmbeddingCreateRequest()
            {
                InputAsList = items,
                Model = OpenAI.GPT3.ObjectModels.Models.TextEmbeddingAdaV2
            });

        if (!embeddingResult.Successful)
        {
            throw new Exception(embeddingResult.Error.Message);
        }

        byte[] serializedEmbeddingResult;

        try
        {
            serializedEmbeddingResult = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(embeddingResult));
        }
        catch (Exception ex)
        {
            throw new Exception("Failed to serialize embedding result.", ex);
        }

        return serializedEmbeddingResult;
    }
    public async Task<byte[]> CalculateEmbedding(string item)
    {
        var embeddingResult = await this._openAIService.Embeddings.CreateEmbedding(
            new OpenAI.GPT3.ObjectModels.RequestModels.EmbeddingCreateRequest()
            {
                Input = item,
                Model = OpenAI.GPT3.ObjectModels.Models.TextEmbeddingAdaV2
            });

        if (!embeddingResult.Successful)
        {
            throw new Exception(embeddingResult.Error.Message);
        }

        byte[] serializedEmbeddingResult;

        try
        {
            serializedEmbeddingResult = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(embeddingResult));
        }
        catch (Exception ex)
        {
            throw new Exception("Failed to serialize embedding result.", ex);
        }

        return serializedEmbeddingResult;
    }

    public async Task<string> ChatWithContext(string context, List<OpenAI.GPT3.ObjectModels.RequestModels.ChatMessage> messages)
    {
        var messageHistory = new List<OpenAI.GPT3.ObjectModels.RequestModels.ChatMessage>()
        {
            new OpenAI.GPT3.ObjectModels.RequestModels.ChatMessage("system", context)
        };

        messageHistory.AddRange(messages);

        var response = await this._openAIService.ChatCompletion.CreateCompletion(
           new OpenAI.GPT3.ObjectModels.RequestModels.ChatCompletionCreateRequest()
           {
               Model = "gpt-3.5-turbo",
               Temperature = (float)1,
               Messages = messageHistory

           });

        if (!response.Successful)
        {
            if (response.Error.Code == "context_length_exceeded")
            {
                throw new FormatException(response.Error.Message);
            }
            throw new Exception(response.Error.Message);
        }

        return response.Choices.FirstOrDefault()?.Message.Content;
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

        for (int i = 0; i < vector1.Length; i++)
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
