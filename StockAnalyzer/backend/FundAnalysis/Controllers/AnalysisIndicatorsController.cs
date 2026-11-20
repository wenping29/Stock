using FundAnalysis.Analysis;
using Microsoft.AspNetCore.Mvc;

namespace FundAnalysis.Controllers;

/// <summary>量化分析：指标清单与在线计算（对齐 /api/indicators）。</summary>
[ApiController]
[Route("api/indicators")]
public sealed class AnalysisIndicatorsController : ControllerBase
{
    public sealed class ComputeRequest
    {
        public List<AnalysisKline>? KlineData { get; set; }
        public List<IndicatorSpec>? Indicators { get; set; }
    }

    public sealed class IndicatorSpec
    {
        public string Name { get; set; } = "";
        public Dictionary<string, double>? Params { get; set; }
    }

    [HttpGet]
    public IActionResult List()
        => Ok(new { success = true, data = IndicatorEngine.List() });

    [HttpPost("compute")]
    public IActionResult Compute([FromBody] ComputeRequest? body)
    {
        if (body?.KlineData is null)
            return BadRequest(new { success = false, error = "klineData is required" });

        var results = new List<object>();
        foreach (var spec in body.Indicators ?? [])
        {
            var indicator = IndicatorEngine.Compute(spec.Name, spec.Params, body.KlineData);
            results.Add(indicator is null
                ? new { name = spec.Name, error = $"Unknown indicator: {spec.Name}" }
                : indicator.ToDto());
        }
        return Ok(new { success = true, data = results });
    }
}