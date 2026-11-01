using Polly;
using Polly.Timeout;

Console.WriteLine("Построение Pipeline: Timeout = 1 с.");

var pipeline = new ResiliencePipelineBuilder()
    .AddTimeout(TimeSpan.FromSeconds(1))
    .Build();

Console.WriteLine("Build завершён. Начало ExecuteAsync.");

try
{
    await pipeline.ExecuteAsync(async cancellationToken =>
    {
        Console.WriteLine("Операция требует 30 секунд.");

        await Task.Delay(
            TimeSpan.FromSeconds(30),
            cancellationToken);

        Console.WriteLine("Операция выполнена успешно.");
    });
}
catch (TimeoutRejectedException)
{
    Console.WriteLine("Ошибка: TimeoutRejectedException");
    Console.WriteLine("Итог: превышен лимит выполнения в 1 секунду.");
}