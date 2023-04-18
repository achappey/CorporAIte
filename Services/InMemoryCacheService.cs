using Microsoft.Extensions.Caching.Memory;

public interface ICacheService
{
    T Get<T>(string key) where T : class;
    void Set<T>(string key, T value, TimeSpan? expiresIn = null);
    void Remove(string key);
}


public class InMemoryCacheService : ICacheService
{
    private readonly IMemoryCache _cache;

    public InMemoryCacheService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public T Get<T>(string key) where T : class
    {
        return _cache.Get<T>(key);
    }

    public void Set<T>(string key, T value, TimeSpan? expiresIn = null)
    {
        var options = new MemoryCacheEntryOptions();
        if (expiresIn.HasValue)
        {
            options.SetAbsoluteExpiration(expiresIn.Value);
        }

        _cache.Set(key, value, options);
    }

    public void Remove(string key)
    {
        _cache.Remove(key);
    }
}
