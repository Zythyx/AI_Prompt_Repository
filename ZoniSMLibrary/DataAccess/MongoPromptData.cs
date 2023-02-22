﻿using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace BlepItLibrary.DataAccess;
public class MongoPromptData : IPromptData
{
    private readonly IDbConnection _db;
    private readonly IMemoryCache _cache;
    private readonly IMongoCollection<Prompt> _promptCollection;
    private const string CacheName = "PromptData";

    public MongoPromptData(IDbConnection db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
        _promptCollection = db.PromptCollection;
    }

    public async Task<List<Prompt>> GetAllPromptsAsync()
    {
        var output = _cache.Get<List<Prompt>>(CacheName);
        if (output is null)
        {
            var results = await _promptCollection.FindAsync(t => t.Archived == false);
            output = results.ToList();

            _cache.Set(CacheName, output, TimeSpan.FromMinutes(1));
        }

        return output;
    }
    
    public async Task<Prompt> GetPromptAsync(string id)
    {
        IAsyncCursor<Prompt> results = await _promptCollection.FindAsync(t => t.Id == id);
        return results.FirstOrDefault();
    }

    public async Task<List<Prompt>> GetUserCreatedPromptsAsync(User user)
    {
        var output = _cache.Get<List<Prompt>>(CacheName + user.Id + "Created");
        if (output is null)
        {
            var results = await _promptCollection.FindAsync(t => t.CreatedBy.Id == user.Id);
            output = results.ToList();

            _cache.Set(CacheName + user.Id + "Created", output, TimeSpan.FromMinutes(1));
        }

        return output;
    }

    public async Task UpdatePromptAsync(Prompt prompt)
    {
        await _promptCollection.ReplaceOneAsync(t => t.Id == prompt.Id, prompt);
        _cache.Remove(CacheName);
    }

    public async Task CreatePromptAsync(Prompt prompt)
    {
        await CreateMultiplePromptsAsync(new[] { prompt });
    }

    public async Task CreateMultiplePromptsAsync(IEnumerable<Prompt> prompts)
    {
        var client = _db.Client;

        using var session = await client.StartSessionAsync();

        session.StartTransaction();

        try
        {
            var db = client.GetDatabase(_db.DbName);
            var promptsInTransaction = db.GetCollection<Prompt>(_db.PromptCollectionName);
            await promptsInTransaction.InsertManyAsync(session, prompts);

            await session.CommitTransactionAsync();
        }
        catch (Exception)
        {
            await session.AbortTransactionAsync();
            throw;
        }
    }
}

