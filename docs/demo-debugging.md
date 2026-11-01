# Отладка PollyDemo: Retry → Timeout

В Rider откройте PollyResearch.sln и запускайте PollyDemo без аргументов:

- **PollyDemo**: Retry снаружи, Timeout внутри. Две InvalidOperationException, затем успех на третьей попытке. MaxRetryAttempts = 2, пауза между повторами 1 с, лимит каждой попытки 5 с.

Проект ссылается на PollyCore.Local, компилирующий оригинальные исходники Polly. Для отладки выберите Debug и PollyDemo в конфигурации запуска Rider. Аргументы оставьте пустыми.

```powershell
dotnet build PollyResearch.sln -c Debug -m:1
dotnet run --project PollyDemo --no-build
```

Таблица ниже описывает цепочку PollyDemo. Демо использует перегрузку ExecuteAsync(Func<CancellationToken, ValueTask>, CancellationToken = default) (ResiliencePipeline.Async.cs:132–164). Polly сама получает ResilienceContext из пула на строке 138, передаёт callback текущий context.CancellationToken на строке 147 и возвращает контекст в finally на строке 163. Явного ResilienceContext в коде демо нет.

## Точки останова

Пути в таблице указаны относительно Polly/src/Polly.Core; номера строк относятся к исходникам 8.9.0. Ставьте точки на исполняемых строках, а не на объявлениях методов.

| Этап и файл:строка | Что посмотреть | Связь с диаграммой последовательности |
|---|---|---|
| Регистрация: Retry/RetryResiliencePipelineBuilderExtensions.cs:30 и Timeout/TimeoutResiliencePipelineBuilderExtensions.cs:48 | options, MaxRetryAttempts = 2, Timeout = 5 с. Передаётся лямбда-фабрика; её тело ещё не исполняется. | Сообщение AddRetry → AddTimeout, участник Builder + extension methods. |
| ResiliencePipelineBuilderBase.cs:115 — AddPipelineComponent | factory и options; после шага _entries.Count сначала 1, затем 2. Фабрики компонентов оборачивают фабрики стратегий через FromStrategy (ResiliencePipelineBuilderExtensions.cs:103 и :79). | «Сохранить фабрики и настройки в _entries». |
| ResiliencePipelineBuilderBase.cs:124 — BuildPipelineComponent | _entries содержит две записи, _used = true. После ConvertAll в components лежат BridgeComponent<object> и BridgeComponent. | Build → BuildPipelineComponent; фабрики вызываются именно сейчас. |
| ResiliencePipelineBuilderBase.cs:143 — CreateComponent | entry.Options и entry.Factory; войдите в factory: создаётся RetryResilienceStrategy<object> либо TimeoutResilienceStrategy. В PipelineComponentFactory.cs:11 и :13 можно увидеть соответствующие Bridge. | Создание двух стратегий и двух разных Bridge при Build. |
| Utils/Pipeline/CompositeComponent.cs:47 и :58 — Create | components.Count = 2; delegatingComponents.Length = 1. После присваивания Next у первого DelegatingComponent указывает на негeneric Timeout Bridge. Его _component — generic Retry Bridge. В Composite FirstComponent будет этим DelegatingComponent. | CompositeComponent.Create связывает Retry → Timeout и возвращает корень. |
| Utils/Pipeline/DelegatingComponent.cs:96 — ExecuteComponent | _component имеет тип BridgeComponent<object>, _strategy == null, Next — BridgeComponent с Timeout. callback и state сохраняются в tuple; Retry передаётся continuation, вызывающий ExecuteNext. | Участник DelegatingComponent + BridgeComponent<object> вызывает Retry.ExecuteCore(continuation). |
| Retry/RetryResilienceStrategy.cs:58 и :81 — ExecuteCore | На строке 58 callback запускается заново для каждой попытки. На :81 смотрите outcome.Exception, handle, isLastAttempt, attempt и RetryCount. Для PollyDemo внутренний attempt = 0, 1, 2; консоль считает с 1. На третьем вызове исключения нет. | Цикл попыток Retry; повторяется внутренняя цепочка, Build не повторяется. |
| Utils/Pipeline/DelegatingComponent.cs:74 и :76 — ExecuteNext | next — негeneric BridgeComponent, bridge.Strategy — TimeoutResilienceStrategy. Строка 76 непосредственно вызывает стратегию. | Сообщение bridge.Strategy.ExecuteCore(callback, context, state). BridgeComponent.ExecuteCore для Timeout здесь обходится. |
| Timeout/TimeoutResilienceStrategy.cs:43, :52 и :74 — ExecuteCore | previousToken — исходный токен; после строки 45 context.CancellationToken — токен cancellationSource. На :52 вызывается внутренняя операция. После отмены на :74 outcome.Exception — OperationCanceledException (возможен производный TaskCanceledException), isCancellationRequested = true, исходный токен не отменён. | Timeout исполняется внутри каждой попытки Retry. В PollyDemo точка :52 достигается трижды. |
| Timeout/TimeoutResilienceStrategy.cs:84 и :90 | Создание TimeoutRejectedException и возврат Outcome с исключением. На :64 уже восстановлен предыдущий токен. | Возврат Outcome от Timeout обратно к Retry. |
| ResiliencePipeline.Async.cs:148, :152 и :159 | :148 — успешный callback даёт Outcome.Void; :152 преобразует перехваченную ошибку операции в Outcome. На :159 GetResultOrRethrow завершает внешний вызов или выбрасывает исключение. | Служебная обёртка callback, опущенная на обзорной UML; затем возврат внешнему приложению. |

Generic Bridge адаптирует Outcome<VoidResult> к Outcome<object> и обратно: Utils/Pipeline/BridgeComponent.TResult.cs:29–49. Поэтому continuation, который получает Retry, включает служебную адаптацию; это не непосредственный пользовательский метод.

В обычном запуске .NET 8 используется DelegatingComponent.ExecuteComponent. В Native AOT возможен ExecuteComponentAot; данный проект не публикуется как Native AOT. Composite также использует невиртуальный DelegatingComponent.Execute, поэтому breakpoint только на DelegatingComponent.ExecuteCore может не сработать.


## Особенности показа

Timeout использует кооперативную отмену. Task.Delay должен получать токен Polly. В текущем примере операция укладывается в лимит 5 с: Timeout участвует в каждой попытке, но не срабатывает. На :74 условие превышения лимита ложно; точки :84 и :90 в обычном запуске не достигаются. Остановки отладчика влияют на время. Два ожидаемых InvalidOperationException обрабатываются Retry.

