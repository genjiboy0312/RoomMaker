using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenCVForUnity.UnityIntegration;

public static class OpenCVEnvHelper
{
    // 단일 파일 비동기 헬퍼
    public static async Task<string> GetFilePathAsync(
        string filepath,
        Action<string, float> progressChanged = null,
        Action<string, string, long> errorOccurred = null,
        bool refresh = false,
        int timeout = 0,
        CancellationToken cancellationToken = default)
    {
        return await OpenCVEnv.GetFilePathTaskAsync(
            filepath,
            progressChanged,
            errorOccurred,
            refresh,
            timeout,
            cancellationToken
        ).ConfigureAwait(false);
    }

    // 다중 파일 비동기 헬퍼
    public static async Task<IReadOnlyList<string>> GetMultipleFilePathsAsync(
        string[] filepaths,
        Action<string> completed = null,
        Action<string, float> progressChanged = null,
        Action<string, string, long> errorOccurred = null,
        bool refresh = false,
        int timeout = 0,
        CancellationToken cancellationToken = default)
    {
        return await OpenCVEnv.GetMultipleFilePathsTaskAsync(
            filepaths,
            completed,
            progressChanged,
            errorOccurred,
            refresh,
            timeout,
            cancellationToken
        ).ConfigureAwait(false);
    }
}
