using Polly;
using Polly.Retry;

Console.WriteLine("Построение Pipeline: Retry -> Timeout.");
var pipeline = new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 2,
        Delay = TimeSpan.FromSeconds(1)
    })
    .AddTimeout(TimeSpan.FromSeconds(5))
    .Build();
Console.WriteLine("Build завершён. Начало ExecuteAsync.");

var attempt = 0;
await pipeline.ExecuteAsync(async cancellationToken =>
{
    attempt++;
    Console.WriteLine($"Попытка №{attempt}");

    // Имитация работы; токен позволяет Timeout отменить задержку.
    await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);

    if (attempt < 3)
    {
        Console.WriteLine("Ошибка: InvalidOperationException");
        throw new InvalidOperationException("Искусственный сбой.");
    }

    Console.WriteLine("Операция выполнена успешно.");
});
Console.WriteLine($"Итог: успех после {attempt} попыток.");
