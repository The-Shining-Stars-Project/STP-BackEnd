using CRM.Application.Services;
using CRM.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CRM.Tests;

public class ScriptDeleteTests
{
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeFileStorage _files = new();
    private readonly ScriptService _service;

    public ScriptDeleteTests()
    {
        _service = new ScriptService(_uow, _files, NullLogger<ScriptService>.Instance);
    }

    [Fact]
    public async Task Delete_removes_the_row_and_its_pdf()
    {
        var script = new Script { Title = "Cinderella" };
        await _uow.Scripts.AddAsync(script);
        await _service.AttachPdfAsync(script.Id, SampleFiles.Pdf(), "c.pdf");
        Assert.Single(_files.Blobs);

        Assert.True(await _service.DeleteAsync(script.Id));

        Assert.Null(await _uow.Scripts.GetByIdAsync(script.Id));
        Assert.Empty(_files.Blobs);
    }

    [Fact]
    public async Task Delete_works_for_a_script_with_no_pdf_and_404s_for_unknown()
    {
        var script = new Script { Title = "Preloaded" };
        await _uow.Scripts.AddAsync(script);
        Assert.True(await _service.DeleteAsync(script.Id));
        Assert.False(await _service.DeleteAsync(Guid.NewGuid()));
    }
}
