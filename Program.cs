using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.IO;
using System.Text.Json;
namespace TGBotV11
{
    class Program
    {
        static TelegramBotClient bot = new TelegramBotClient("8533175061:AAHDNDI0iGK1zWP7qc7X_JmNsaoNQqI-dTc");

        static Dictionary<long, string> userStates = new Dictionary<long, string>();
        static Dictionary<long, List<string>> userWorkouts = new Dictionary<long, List<string>>();
        static string filePath = "data.json";

        static void Main(string[] args)
        {
            LoadData();
            bot.StartReceiving(HandleUpdate, HandleError);

            Console.WriteLine("Бот запущен...");
            Console.ReadLine();
        }

        static async Task HandleUpdate(ITelegramBotClient botClient, Update update, CancellationToken ct)
        {
            if (update.Type != UpdateType.Message || update.Message?.Text == null)
                return;

            var chatId = update.Message.Chat.Id;
            var text = update.Message.Text;

            var keyboard = new ReplyKeyboardMarkup(new[]
            {
                new KeyboardButton[] { "🏋️ Добавить тренировку" },
                new KeyboardButton[] { "📊 Мои отчёты" },
                new KeyboardButton[] { "📈 Статистика" },
                new KeyboardButton[] { "🗑️ Удалить последнюю" }
            })
            {
                ResizeKeyboard = true
            };

            if (text == "/start")
            {
                await botClient.SendMessage(chatId, "Привет! Выбери действие:", replyMarkup: keyboard);
                return;
            }

            // ➕ Добавить тренировку
            if (text == "🏋️ Добавить тренировку")
            {
                userStates[chatId] = "waiting_workout";
                await botClient.SendMessage(chatId, "Что ты тренировал?");
                return;
            }

            // ввод тренировки
            if (userStates.ContainsKey(chatId) && userStates[chatId] == "waiting_workout")
            {
                userStates[chatId] = "waiting_time";

                if (!userWorkouts.ContainsKey(chatId))
                    userWorkouts[chatId] = new List<string>();

                userWorkouts[chatId].Add(text);
                

                await botClient.SendMessage(chatId, "Сколько длилась тренировка (в минутах)?");
                return;
            }

            // ввод времени
            if (userStates.ContainsKey(chatId) && userStates[chatId] == "waiting_time")
            {
                var lastIndex = userWorkouts[chatId].Count - 1;

                string date = DateTime.Now.ToString("dd.MM");

                userWorkouts[chatId][lastIndex] =
                    $"{date} — {userWorkouts[chatId][lastIndex]} — {text} мин";

                userStates[chatId] = "";
                SaveData();

                await botClient.SendMessage(chatId, "Тренировка сохранена ✅", replyMarkup: keyboard);
                return;
            }

            // 📊 отчёты
            if (text == "📊 Мои отчёты")
            {
                if (!userWorkouts.ContainsKey(chatId) || userWorkouts[chatId].Count == 0)
                {
                    await botClient.SendMessage(chatId, "У тебя пока нет тренировок");
                    return;
                }

                string report = "Твои тренировки:\n\n";

                int i = 1;
                foreach (var w in userWorkouts[chatId])
                {
                    report += i + ") " + w + "\n";
                    i++;
                }

                await botClient.SendMessage(chatId, report);
                return;
            }

            // 📈 статистика
            if (text == "📈 Статистика")
            {
                if (!userWorkouts.ContainsKey(chatId) || userWorkouts[chatId].Count == 0)
                {
                    await botClient.SendMessage(chatId, "Нет данных для статистики");
                    return;
                }

                int total = userWorkouts[chatId].Count;
                int totalMinutes = 0;

                foreach (var w in userWorkouts[chatId])
                {
                    var parts = w.Split(' ');
                    int minutes = int.Parse(parts[^2]); // берём число перед "мин"
                    totalMinutes += minutes;
                }

                string stats = $"📈 Статистика:\n\n" +
                               $"Тренировок: {total}\n" +
                               $"Общее время: {totalMinutes} мин";

                await botClient.SendMessage(chatId, stats);
                return;
            }

            // 🗑️ удалить последнюю
            if (text == "🗑️ Удалить последнюю")
            {
                if (!userWorkouts.ContainsKey(chatId) || userWorkouts[chatId].Count == 0)
                {
                    await botClient.SendMessage(chatId, "Нечего удалять");
                    return;
                }

                userWorkouts[chatId].RemoveAt(userWorkouts[chatId].Count - 1);
                SaveData();

                await botClient.SendMessage(chatId, "Последняя тренировка удалена 🗑️");
                return;
            }
        }

        static Task HandleError(ITelegramBotClient botClient, Exception exception, CancellationToken ct)
        {
            Console.WriteLine(exception.Message);
            return Task.CompletedTask;
        }
        static void SaveData()
        {
            var json = JsonSerializer.Serialize(userWorkouts);
            File.WriteAllText(filePath, json);
        }

        static void LoadData()
        {
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                userWorkouts = JsonSerializer.Deserialize<Dictionary<long, List<string>>>(json)
                               ?? new Dictionary<long, List<string>>();
            }
        }
    }
}