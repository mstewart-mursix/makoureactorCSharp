using System.Collections.Generic;
using System.Text;
using System.Text.Json;

using FluentAssertions;

using MakouReactor.AI;
using MakouReactor.AI.Backends;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests;

public class SceneGenerationRefineAndProgressTests
{
    private const string SceneJson =
        "{\"meta\":{\"title\":\"Ambush\",\"model\":\"fake\",\"version\":\"1.0\"}," +
        "\"actors\":[{\"id\":\"cloud\",\"position\":{\"x\":10,\"y\":20}}]," +
        "\"dialog\":[{\"speakerId\":\"cloud\",\"text\":\"Let's move.\"}]}";

    private const string RefinedJson =
        "{\"meta\":{\"title\":\"Ambush\",\"model\":\"fake\",\"version\":\"1.0\"}," +
        "\"actors\":[{\"id\":\"cloud\",\"position\":{\"x\":10,\"y\":20}}]," +
        "\"dialog\":[{\"speakerId\":\"cloud\",\"text\":\"Move out. Now.\"}]}";

    private sealed class Script : ILLMBackend
    {
        private readonly Queue<LLMRawResult> _results;
        public List<LLMRequest> Requests { get; } = new();
        public Script(params LLMRawResult[] results) => _results = new Queue<LLMRawResult>(results);

        public Task<LLMRawResult> RequestScenePlanAsync(LLMRequest req)
        {
            Requests.Add(req);
            return Task.FromResult(_results.Count > 1 ? _results.Dequeue() : _results.Peek());
        }

        public Task<LLMRawResult> RequestScenePlanAsync(LLMRequest req, IProgress<LLMProgressEvent>? progress)
        {
            progress?.Report(new LLMProgressEvent(LLMProgressPhase.Responding, "backend says hi"));
            return RequestScenePlanAsync(req);
        }

        public void CancelAll() { }
        public void Dispose() { }
    }

    private sealed class Collect : IProgress<LLMProgressEvent>
    {
        public List<LLMProgressEvent> Events { get; } = new();
        public void Report(LLMProgressEvent value) => Events.Add(value);
    }

    private static LLMRawResult Good(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return new LLMRawResult { Ok = true, Raw = bytes, Json = JsonDocument.Parse(bytes) };
    }

    private static ScenePrompt Prompt() => new() { UserText = "Cloud gets ambushed" };

    [Fact]
    public async Task progress_reports_phases_in_order_for_a_clean_run()
    {
        var progress = new Collect();
        var service = new SceneGenerationService(new LLMConfig(), new Script(Good(SceneJson)));

        await service.GenerateAsync(Prompt(), progress: progress);

        progress.Events.Select(e => e.Phase).Should().Equal(
            LLMProgressPhase.Starting,
            LLMProgressPhase.Waiting,
            LLMProgressPhase.Responding,
            LLMProgressPhase.Parsing,
            LLMProgressPhase.Validating,
            LLMProgressPhase.Done);
        progress.Events[2].Detail.Should().Be("backend says hi");
    }

    [Fact]
    public async Task progress_reports_a_repair_with_the_reason_and_attempt_number()
    {
        var progress = new Collect();
        var backend = new Script(Good("{\"unexpected\":true}"), Good(SceneJson));
        var service = new SceneGenerationService(new LLMConfig { MaxRepairAttempts = 1 }, backend);

        var result = await service.GenerateAsync(Prompt(), progress: progress);

        result.Ok.Should().BeTrue(result.Error);
        var repair = progress.Events.Should().ContainSingle(e => e.Phase == LLMProgressPhase.Repairing).Subject;
        repair.Attempt.Should().Be(2);
        repair.Detail.Should().Contain("meta");
        progress.Events[^1].Phase.Should().Be(LLMProgressPhase.Done);
    }

    [Fact]
    public async Task transport_failure_reports_done_with_the_error()
    {
        var progress = new Collect();
        var service = new SceneGenerationService(new LLMConfig(),
            new Script(LLMRawResult.Failure(0, "not logged in")));

        await service.GenerateAsync(Prompt(), progress: progress);

        progress.Events[^1].Should().Be(new LLMProgressEvent(LLMProgressPhase.Done, "not logged in", 1));
    }

    [Fact]
    public async Task non_json_model_reply_is_repaired_instead_of_treated_as_transport_failure()
    {
        // CodexCliBackend reports "not valid JSON" as Ok=false but keeps the model's text in Raw.
        var chatty = LLMRawResult.Failure(0, "Model output was not valid JSON: Sure! Here you go",
            Encoding.UTF8.GetBytes("Sure! Here you go"));
        var backend = new Script(chatty, Good(SceneJson));
        var service = new SceneGenerationService(new LLMConfig { MaxRepairAttempts = 1 }, backend);

        var result = await service.GenerateAsync(Prompt());

        result.Ok.Should().BeTrue(result.Error);
        result.Attempts.Should().HaveCount(2);
        backend.Requests[1].UserPrompt.Should().Contain("Sure! Here you go").And.Contain("not valid JSON");
    }

    [Fact]
    public async Task refine_sends_previous_json_and_instruction_and_returns_the_updated_plan()
    {
        var backend = new Script(Good(SceneJson));
        var service = new SceneGenerationService(new LLMConfig(), backend);
        var first = await service.GenerateAsync(Prompt());
        var refinedBackend = new Script(Good(RefinedJson));
        var refiner = new SceneGenerationService(new LLMConfig(), refinedBackend);

        var refined = await refiner.RefineAsync(first, "make Cloud more urgent", Prompt());

        refined.Ok.Should().BeTrue(refined.Error);
        refined.Plan!.Dialog[0].Text.Should().Be("Move out. Now.");
        var sent = refinedBackend.Requests[0].UserPrompt;
        sent.Should().Contain("Cloud gets ambushed")
            .And.Contain("Current scene plan JSON:").And.Contain("Let's move.")
            .And.Contain("Refinement request:").And.Contain("make Cloud more urgent")
            .And.EndWith("Return the complete updated JSON object only.");
    }

    [Fact]
    public async Task refine_goes_through_the_repair_loop()
    {
        var first = await new SceneGenerationService(new LLMConfig(), new Script(Good(SceneJson))).GenerateAsync(Prompt());
        var backend = new Script(Good("{\"unexpected\":true}"), Good(RefinedJson));
        var service = new SceneGenerationService(new LLMConfig { MaxRepairAttempts = 1 }, backend);

        var refined = await service.RefineAsync(first, "tighten the dialogue", Prompt());

        refined.Ok.Should().BeTrue(refined.Error);
        refined.Attempts.Should().HaveCount(2);
        backend.Requests[1].UserPrompt.Should().Contain("Fix instructions:");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task refine_rejects_a_blank_instruction_without_calling_the_backend(string instruction)
    {
        var backend = new Script(Good(SceneJson));
        var service = new SceneGenerationService(new LLMConfig(), backend);
        var first = await service.GenerateAsync(Prompt());
        backend.Requests.Clear();

        var refined = await service.RefineAsync(first, instruction, Prompt());

        refined.Ok.Should().BeFalse();
        backend.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task refine_without_a_previous_plan_fails_cleanly()
    {
        var service = new SceneGenerationService(new LLMConfig(), new Script(Good(SceneJson)));

        var refined = await service.RefineAsync(
            SceneGenerationResult.Failure("boom"), "change something", Prompt());

        refined.Ok.Should().BeFalse();
        refined.Error.Should().Contain("no previous plan");
    }
}
