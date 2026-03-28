using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.IO;
using System.Text.Json;

namespace TGBotV11
{
    class Program
    {
        static TelegramBotClient bot;

        static Dictionary<long, string> userStates = new();
        static Dictionary<long, List<string>> userWorkouts = new();
        static Dictionary<long, int> userGoals = new();

        static string filePath = "data.json";

        static async Task Main(string[] args)
        {
            var token = Environment.GetEnvironmentVariable("BOT_TOKEN");

            bot = new TelegramBotClient(token);

            LoadData();

            bot.StartReceiving(HandleUpdate, HandleError);

            Console.WriteLine("Бот запущен...");

            await Task.Delay(-1);
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
                new KeyboardButton[] { "🎯 Моя цель" },
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

            // 🎯 цель
            if (text == "🎯 Моя цель")
            {
                userStates[chatId] = "waiting_goal";
                await botClient.SendMessage(chatId, "Сколько тренировок в неделю твоя цель?");
                return;
            }

            if (userStates.ContainsKey(chatId) && userStates[chatId] == "waiting_goal")
            {
                if (int.TryParse(text, out int goal))
                {
                    userGoals[chatId] = goal;
                    userStates[chatId] = "";

                    await botClient.SendMessage(chatId, $"Цель установлена: {goal} тренировок 🎯");
                }
                else
                {
                    await botClient.SendMessage(chatId, "Введи число");
                }
                return;
            }

            // ➕ Добавить тренировку
            if (text == "🏋️ Добавить тренировку")
            {
                userStates[chatId] = "waiting_workout";
                await botClient.SendMessage(chatId, "Что ты тренировал?");
                return;
            }

            if (userStates.ContainsKey(chatId) && userStates[chatId] == "waiting_workout")
            {
                userStates[chatId] = "waiting_time";

                if (!userWorkouts.ContainsKey(chatId))
                    userWorkouts[chatId] = new List<string>();

                userWorkouts[chatId].Add(text);

                await botClient.SendMessage(chatId, "Сколько минут?");
                return;
            }

            if (userStates.ContainsKey(chatId) && userStates[chatId] == "waiting_time")
            {
                if (!int.TryParse(text, out int minutes))
                {
                    await botClient.SendMessage(chatId, "Введи число!");
                    return;
                }

                var lastIndex = userWorkouts[chatId].Count - 1;
                string date = DateTime.Now.ToString("dd.MM");

                userWorkouts[chatId][lastIndex] =
                    $"{date}|{userWorkouts[chatId][lastIndex]}|{minutes}";

                userStates[chatId] = "";
                SaveData();

                await botClient.SendMessage(chatId, "Сохранено ✅", replyMarkup: keyboard);
                return;
            }

            // 📊 отчёты
            if (text == "📊 Мои отчёты")
            {
                if (!userWorkouts.ContainsKey(chatId) || userWorkouts[chatId].Count == 0)
                {
                    await botClient.SendMessage(chatId, "Нет тренировок");
                    return;
                }

                string report = "";

                int i = 1;
                foreach (var w in userWorkouts[chatId])
                {
                    var parts = w.Split('|');
                    report += $"{i}) {parts[0]} — {parts[1]} — {parts[2]} мин\n";
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
                    await botClient.SendMessage(chatId, "Нет данных");
                    return;
                }

                int total = userWorkouts[chatId].Count;
                int totalMinutes = 0;

                foreach (var w in userWorkouts[chatId])
                {
                    var parts = w.Split('|');
                    if (int.TryParse(parts[2], out int m))
                        totalMinutes += m;
                }

                int goal = userGoals.ContainsKey(chatId) ? userGoals[chatId] : 0;

                string stats = $"📈 Статистика:\n\n" +
                               $"Тренировок: {total}\n" +
                               $"Минут: {totalMinutes}";

                if (goal > 0)
                {
                    stats += $"\nЦель: {goal}\nПрогресс: {total}/{goal}";
                }

                await botClient.SendMessage(chatId, stats);
                return;
            }

            // 🗑️ удалить
            if (text == "🗑️ Удалить последнюю")
            {
                if (!userWorkouts.ContainsKey(chatId) || userWorkouts[chatId].Count == 0)
                {
                    await botClient.SendMessage(chatId, "Нечего удалять");
                    return;
                }

                userWorkouts[chatId].RemoveAt(userWorkouts[chatId].Count - 1);
                SaveData();

                await botClient.SendMessage(chatId, "Удалено 🗑️");
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