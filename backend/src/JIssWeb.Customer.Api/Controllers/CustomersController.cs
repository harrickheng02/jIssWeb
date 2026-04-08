using JIssWeb.Common;
using JIssWeb.Common.Helpers;
using JIssWeb.Common.Options;
using JIssWeb.Customer.Api.Models;
using JIssWeb.Customer.Api.Mongo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace JIssWeb.Customer.Api.Controllers;

[ApiController]
[Route("api/customers")]
[Authorize]
public class CustomersController : ControllerBase
{
    private readonly IMongoCollection<CustomerRecord> _customers;

    public CustomersController(IMongoClient mongoClient, IOptions<MongoSettings> mongoOptions)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _customers = db.GetCollection<CustomerRecord>(CustomerMongoSetup.CollectionName);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResult<CustomerRecord>>> Create([FromBody] CreateCustomerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(ApiResult<CustomerRecord>.Fail("名称不能为空", "INVALID_INPUT"));

        var now = DateTime.UtcNow;
        var doc = new CustomerRecord
        {
            Id = ObjectId.GenerateNewId().ToString(),
            OwnerUserId = User.GetUserId(),
            Name = request.Name.Trim(),
            Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        await _customers.InsertOneAsync(doc);
        return Ok(ApiResult<CustomerRecord>.Ok(doc));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResult<List<CustomerRecord>>>> List()
    {
        var owner = User.GetUserId();
        var list = await _customers.Find(x => x.OwnerUserId == owner)
            .SortByDescending(x => x.CreatedAtUtc)
            .ToListAsync();
        return Ok(ApiResult<List<CustomerRecord>>.Ok(list));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResult<CustomerRecord>>> GetById(string id)
    {
        if (!ObjectId.TryParse(id, out _))
            return NotFound(ApiResult<CustomerRecord>.Fail("未找到", "NOT_FOUND"));

        var owner = User.GetUserId();
        var doc = await _customers.Find(x => x.Id == id).FirstOrDefaultAsync();
        if (doc is null || doc.OwnerUserId != owner)
            return NotFound(ApiResult<CustomerRecord>.Fail("未找到", "NOT_FOUND"));

        return Ok(ApiResult<CustomerRecord>.Ok(doc));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResult<CustomerRecord>>> Update(string id, [FromBody] UpdateCustomerRequest request)
    {
        if (!ObjectId.TryParse(id, out _))
            return NotFound(ApiResult<CustomerRecord>.Fail("未找到", "NOT_FOUND"));
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(ApiResult<CustomerRecord>.Fail("名称不能为空", "INVALID_INPUT"));

        var owner = User.GetUserId();
        var doc = await _customers.Find(x => x.Id == id).FirstOrDefaultAsync();
        if (doc is null || doc.OwnerUserId != owner)
            return NotFound(ApiResult<CustomerRecord>.Fail("未找到", "NOT_FOUND"));

        doc.Name = request.Name.Trim();
        doc.Remark = string.IsNullOrWhiteSpace(request.Remark) ? null : request.Remark.Trim();
        doc.UpdatedAtUtc = DateTime.UtcNow;
        await _customers.ReplaceOneAsync(x => x.Id == id, doc);
        return Ok(ApiResult<CustomerRecord>.Ok(doc));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResult<string>>> Delete(string id)
    {
        if (!ObjectId.TryParse(id, out _))
            return NotFound(ApiResult<string>.Fail("未找到", "NOT_FOUND"));

        var owner = User.GetUserId();
        var doc = await _customers.Find(x => x.Id == id).FirstOrDefaultAsync();
        if (doc is null || doc.OwnerUserId != owner)
            return NotFound(ApiResult<string>.Fail("未找到", "NOT_FOUND"));

        await _customers.DeleteOneAsync(x => x.Id == id);
        return Ok(ApiResult<string>.Ok("deleted"));
    }
}

public class CreateCustomerRequest
{
    public string? Name { get; set; }
    public string? Remark { get; set; }
}

public class UpdateCustomerRequest
{
    public string? Name { get; set; }
    public string? Remark { get; set; }
}
