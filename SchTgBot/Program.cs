using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

IConfigurationRoot config = new ConfigurationBuilder()
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

string token = config["TELEGRAM_BOT_TOKEN"]
    ?? throw new Exception("Set TELEGRAM_BOT_TOKEN user secret");

var bot = new TelegramBotClient(token);
var me = await bot.GetMe();
Console.WriteLine($"Запущен @{me.Username}");

using var cts = new CancellationTokenSource();

// (опционально) зарегистрируем команду в меню бота
await bot.SetMyCommands(
[
    new BotCommand { Command = "sch", Description = "Опрос на неделю: /sch [N]" }
], cancellationToken: cts.Token);

// принимаем все апдейты
var receiverOptions = new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() };
bot.StartReceiving(HandleUpdateAsync, HandleErrorAsync, receiverOptions, cts.Token);

Console.WriteLine("Готов. Нажми Enter для выхода.");
Console.ReadLine();
cts.Cancel();

async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
{
    if (update.Message is not { } msg) return;
    if (msg.Text is null) return;

    // Обрабатываем только команды в начале сообщения
    var isCmd = msg.Entities?.Any(e => e.Type == MessageEntityType.BotCommand && e.Offset == 0) == true;
    if (!isCmd) return;

    var entity = msg.Entities!.First(e => e.Type == MessageEntityType.BotCommand);
    var cmdWithSlash = msg.Text.Substring(entity.Offset, entity.Length);   // "/sch" или "/sch@Bot"
    var split = cmdWithSlash.Split('@', 2, StringSplitOptions.RemoveEmptyEntries);
    var cmd = split[0][1..]; // убираем '/'

    // Если команда адресована другому боту — игнорируем
    if (split.Length == 2 && !split[1].Equals(me.Username, StringComparison.OrdinalIgnoreCase)) return;

    if (cmd == "sch")
    {
        var weekOffset = 0;
        const int MIN_OFFSET = -12;
        const int MAX_OFFSET = 52;

        var argsText = msg.Text[(entity.Offset + entity.Length)..].Trim();

        if (!string.IsNullOrWhiteSpace(argsText))
        {
            if (!int.TryParse(argsText.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0],
                              NumberStyles.Integer, CultureInfo.InvariantCulture,
                              out weekOffset))
            {
                await bot.SendMessage(msg.Chat.Id,
                    "Использование: /sch [N], где N — число недель вперёд (0, 1, 2, …).",
                    cancellationToken: ct);
                return;
            }
        }

        if (weekOffset < MIN_OFFSET || weekOffset > MAX_OFFSET)
        {
            await bot.SendMessage(msg.Chat.Id,
                $"Диапазон N: {MIN_OFFSET}…{MAX_OFFSET}. Попробуй ещё раз.",
                cancellationToken: ct);
            return;
        }

        // формируем варианты: Пн–Вс текущей недели с датами (даже если часть дней уже прошла)
        TimeZoneInfo.ClearCachedData();
        Environment.SetEnvironmentVariable("TZ", "Europe/Moscow");
        var options = BuildCurrentWeekOptions(DateTime.Now, weekOffset);

        // создаём опрос
        var pollMsg = await bot.SendPoll(
            chatId: msg.Chat.Id,
            question: "Когда играем?",
            options: options,
            allowsMultipleAnswers: true,
            isAnonymous: false,
            messageThreadId: msg.MessageThreadId    // чтобы корректно работать в топиках
        );

        // сразу закрепляем (нужны права админа can_pin_messages)
        try
        {
            await bot.PinChatMessage(
                chatId: pollMsg.Chat.Id,
                messageId: pollMsg.MessageId
            // disableNotification: false  // можно указать явно при желании
            );
        }
        catch (ApiRequestException ex)
        {
            await bot.SendMessage(
                chatId: pollMsg.Chat.Id,
                text: "Опрос создан, но не удалось закрепить — у меня нет прав администратора с возможностью закреплять сообщения.",
                cancellationToken: ct
            );
            Console.WriteLine($"Pin failed: [{ex.ErrorCode}] {ex.Message}");
        }
    }
}

Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken ct)
{
    var err = ex switch
    {
        ApiRequestException apiEx => $"Telegram API Error [{apiEx.ErrorCode}]: {apiEx.Message}",
        _ => ex.ToString()
    };
    Console.WriteLine(err);
    return Task.CompletedTask;
}

// ---- helpers ----
static IEnumerable<InputPollOption> BuildCurrentWeekOptions(DateTime now, int weekOffset = 0)
{
    // Старт недели = понедельник
    int deltaFromMonday = ((int)now.DayOfWeek + 6) % 7; // вс=0 → 6, пн=1 → 0
    var monday = now.Date.AddDays(-deltaFromMonday).AddDays(7 * weekOffset);

    string[] dayRu = { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };
    var opts = new List<string>(7);
    for (int i = 0; i < 7; i++)
    {
        var d = monday.AddDays(i);
        opts.Add($"{dayRu[i]} {d:dd.MM}");
    }
    return opts.Select(o => new InputPollOption(o));
}