using System.IO;

using FluentAssertions;

using MakouReactor.AI.Config;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.Config;

public class LLMConfigLoaderTests
{
    [Fact]
    public void load_defaults_when_file_missing()
    {
        var config = LLMConfigLoader.Load(Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid() + ".json"));

        config.Backend.Should().Be("codex");
        config.CodexExecutable.Should().Be("codex");
        config.CodexModel.Should().BeEmpty();
        config.TimeoutMs.Should().Be(0);
        config.Endpoint.Should().Be("http://localhost:1234/v1/chat/completions");
        config.ApiKey.Should().Be("lm-studio");
        config.Model.Should().Be("qwen/qwen3-coder-30b");
        config.MaxRepairAttempts.Should().Be(1);
    }

    [Fact]
    public void load_codex_backend_settings()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "llm_config_test_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var json = @"{
  ""backend"": ""codex"",
  ""codex_executable"": ""C:\\tools\\codex.cmd"",
  ""codex_model"": ""gpt-5-codex"",
  ""timeout_ms"": 120000,
  ""max_repair_attempts"": 3
}";
            File.WriteAllText(tempFile, json);

            var config = LLMConfigLoader.Load(tempFile);

            config.Backend.Should().Be("codex");
            config.CodexExecutable.Should().Be(@"C:\tools\codex.cmd");
            config.CodexModel.Should().Be("gpt-5-codex");
            config.TimeoutMs.Should().Be(120000);
            config.MaxRepairAttempts.Should().Be(3);
            LLMConfig.Load(tempFile).MaxRepairAttempts.Should().Be(3);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void load_http_backend_selection()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "llm_config_test_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(tempFile, @"{ ""backend"": ""http"" }");

            var config = LLMConfigLoader.Load(tempFile);

            config.Backend.Should().Be("http");
            config.Endpoint.Should().Be("http://localhost:1234/v1/chat/completions");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void load_from_valid_json_file()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "llm_config_test_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var json = @"{
  ""endpoint"": ""https://test.api.example.com/v1"",
  ""api_key"": ""test-key-123"",
  ""model"": ""gpt-4o-mini""
}";
            File.WriteAllText(tempFile, json);

            var config = LLMConfigLoader.Load(tempFile);

            config.Endpoint.Should().Be("https://test.api.example.com/v1/chat/completions"); // auto-appended
            config.ApiKey.Should().Be("test-key-123");
            config.Model.Should().Be("gpt-4o-mini");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void load_partial_file_merges_with_defaults()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "llm_config_test_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var json = @"{
  ""model"": ""custom-model""
}";
            File.WriteAllText(tempFile, json);

            var config = LLMConfigLoader.Load(tempFile);

            config.Model.Should().Be("custom-model");
            config.Endpoint.Should().Be("http://localhost:1234/v1/chat/completions"); // fallback
            config.ApiKey.Should().Be("lm-studio"); // fallback
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void load_invalid_json_falls_back_to_defaults()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "llm_config_test_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(tempFile, "{invalid json}");

            var config = LLMConfigLoader.Load(tempFile);

            config.Should().NotBeNull();
            config.Endpoint.Should().Be("http://localhost:1234/v1/chat/completions");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void endpoint_normalization_appends_chat_completions()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "llm_config_test_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var json = @"{
  ""endpoint"": ""https://api.example.com/v1"",
  ""api_key"": ""key"",
  ""model"": ""model""
}";
            File.WriteAllText(tempFile, json);

            var config = LLMConfigLoader.Load(tempFile);

            config.Endpoint.Should().Be("https://api.example.com/v1/chat/completions");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void load_core_config_from_file()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "llm_config_test_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var json = @"{
  ""endpoint"": ""https://api.example.com/v1"",
  ""apiKey"": ""key"",
  ""model"": ""model""
}";
            File.WriteAllText(tempFile, json);

            var config = LLMConfig.Load(tempFile);

            config.Endpoint.Should().Be("https://api.example.com/v1");
            config.ApiKey.Should().Be("key");
            config.Model.Should().Be("model");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
