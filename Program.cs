using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace TGBotV11
{
    class Program
    {
        static TelegramBotClient bot = null!;
        static string connectionString = "Data Source=fitness.db";

        static Dictionary<long, string> userStates = new();
        static Dictionary<long, string> tempWorkoutNames = new();

        static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            app.UseDefaultFiles();
            app.UseStaticFiles();

            var port = Environment.GetEnvironmentVariable("PORT") ?? "3000";
            var port = Environment.GetEnvironmentVariable("PORT") ?? "3000";
            app.Urls.Add("http://0.0.0.0:" + port);
            app.RunAsync();

            var token = Environment.GetEnvironmentVariable("BOT_TOKEN");

            if (string.IsNullOrWhiteSpace(token))
            {
                Console.WriteLine("BOT_TOKEN не найден");
                return;
            }

            bot = new TelegramBotClient(token);

            InitDatabase();

            bot.StartReceiving(HandleUpdate, HandleError);

            Console.WriteLine("Бот запущен...");
            await Task.Delay(-1);
        }

        static async Task HandleUpdate(ITelegramBotClient botClient, Update update, CancellationToken ct)
        {
            if (update.Type != UpdateType.Message || update.Message?.Text == null)
                return;

            var chatId = update.Message.Chat.Id;
            var text = update.Message.Text.Trim();

            var keyboard = new ReplyKeyboardMarkup(new[]
            {
                new KeyboardButton[] { "🏋️ Добавить тренировку" },
                new KeyboardButton[] { "📊 Мои отчёты" },
                new KeyboardButton[] { "📈 Статистика" },
                new KeyboardButton[] { "🎯 Моя цель" },
                new KeyboardButton[] { "🍗 Питание" },
                new KeyboardButton[] { "😵 Самочувствие" },
                new KeyboardButton[] { "🔥 Стрик" },
                new KeyboardButton[] { "🏆 Достижения" },
                new KeyboardButton[] { "🗑️ Удалить последнюю" }
            })
            {
                ResizeKeyboard = true
            };

            if (text == "/start")
            {
                await botClient.SendMessage(
                    chatId,
                    "Привет! Я фитнес-бот.\nВыбери действие:",
                    replyMarkup: keyboard,
                    cancellationToken: ct
                );
                return;
            }

            if (text == "🏋️ Добавить тренировку")
            {
                userStates[chatId] = "waiting_workout_name";
                await botClient.SendMessage(chatId, "Что ты тренировал?", cancellationToken: ct);
                return;
            }

            if (userStates.TryGetValue(chatId, out string? state) && state == "waiting_workout_name")
            {
                tempWorkoutNames[chatId] = text;
                userStates[chatId] = "waiting_workout_minutes";
                await botClient.SendMessage(chatId, "Сколько минут длилась тренировка?", cancellationToken: ct);
                return;
            }

            if (userStates.TryGetValue(chatId, out state) && state == "waiting_workout_minutes")
            {
                if (!int.TryParse(text, out int minutes) || minutes <= 0)
                {
                    await botClient.SendMessage(chatId, "Введи нормальное число минут.", cancellationToken: ct);
                    return;
                }

                if (!tempWorkoutNames.ContainsKey(chatId))
                {
                    userStates.Remove(chatId);
                    await botClient.SendMessage(chatId, "Ошибка. Начни заново.", replyMarkup: keyboard, cancellationToken: ct);
                    return;
                }

                AddWorkout(chatId, tempWorkoutNames[chatId], minutes);
                tempWorkoutNames.Remove(chatId);
                userStates.Remove(chatId);

                await botClient.SendMessage(chatId, "Тренировка сохранена ✅", replyMarkup: keyboard, cancellationToken: ct);
                return;
            }

            if (text == "🎯 Моя цель")
            {
                userStates[chatId] = "waiting_goal";
                await botClient.SendMessage(chatId, "Сколько тренировок в неделю твоя цель?", cancellationToken: ct);
                return;
            }

            if (userStates.TryGetValue(chatId, out state) && state == "waiting_goal")
            {
                if (!int.TryParse(text, out int goal) || goal <= 0)
                {
                    await botClient.SendMessage(chatId, "Введи нормальное число.", cancellationToken: ct);
                    return;
                }

                SaveGoal(chatId, goal);
                userStates.Remove(chatId);

                await botClient.SendMessage(chatId, $"Цель сохранена: {goal} тренировок в неделю 🎯", replyMarkup: keyboard, cancellationToken: ct);
                return;
            }

            if (text == "🍗 Питание")
            {
                userStates[chatId] = "waiting_food";
                await botClient.SendMessage(chatId, "Введи через пробел:\nкалории белки жиры углеводы\n\nПример:\n2200 160 70 240", cancellationToken: ct);
                return;
            }

            if (userStates.TryGetValue(chatId, out state) && state == "waiting_food")
            {
                var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length != 4 ||
                    !int.TryParse(parts[0], out int calories) ||
                    !int.TryParse(parts[1], out int protein) ||
                    !int.TryParse(parts[2], out int fat) ||
                    !int.TryParse(parts[3], out int carbs))
                {
                    await botClient.SendMessage(chatId, "Формат неверный. Пример:\n2200 160 70 240", cancellationToken: ct);
                    return;
                }

                SaveFood(chatId, calories, protein, fat, carbs);
                userStates.Remove(chatId);

                await botClient.SendMessage(chatId, "Питание сохранено 🍗", replyMarkup: keyboard, cancellationToken: ct);
                return;
            }

            if (text == "😵 Самочувствие")
            {
                userStates[chatId] = "waiting_feeling";
                await botClient.SendMessage(chatId, "Напиши, как ты себя чувствуешь сегодня или после тренировки.", cancellationToken: ct);
                return;
            }

            if (userStates.TryGetValue(chatId, out state) && state == "waiting_feeling")
            {
                SaveFeeling(chatId, text);
                userStates.Remove(chatId);

                await botClient.SendMessage(chatId, "Самочувствие сохранено 👍", replyMarkup: keyboard, cancellationToken: ct);
                return;
            }

            if (text == "📊 Мои отчёты")
            {
                string report = GetWorkoutReport(chatId);
                await botClient.SendMessage(chatId, report, cancellationToken: ct);
                return;
            }

            if (text == "📈 Статистика")
            {
                string stats = GetStats(chatId);
                await botClient.SendMessage(chatId, stats, cancellationToken: ct);
                return;
            }

            if (text == "🔥 Стрик")
            {
                int streak = GetWorkoutStreak(chatId);
                await botClient.SendMessage(chatId, $"🔥 Текущий стрик: {streak} дн.", cancellationToken: ct);
                return;
            }

            if (text == "🏆 Достижения")
            {
                string achievements = GetAchievements(chatId);
                await botClient.SendMessage(chatId, achievements, cancellationToken: ct);
                return;
            }

            if (text == "🗑️ Удалить последнюю")
            {
                bool deleted = DeleteLastWorkout(chatId);

                if (deleted)
                    await botClient.SendMessage(chatId, "Последняя тренировка удалена 🗑️", cancellationToken: ct);
                else
                    await botClient.SendMessage(chatId, "Удалять нечего.", cancellationToken: ct);

                return;
            }
        }

        static Task HandleError(ITelegramBotClient botClient, Exception exception, CancellationToken ct)
        {
            Console.WriteLine(exception.ToString());
            return Task.CompletedTask;
        }

        static void InitDatabase()
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS workouts (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    userId INTEGER NOT NULL,
                    date TEXT NOT NULL,
                    name TEXT NOT NULL,
                    minutes INTEGER NOT NULL
                );

                CREATE TABLE IF NOT EXISTS goals (
                    userId INTEGER PRIMARY KEY,
                    weeklyGoal INTEGER NOT NULL
                );

                CREATE TABLE IF NOT EXISTS food (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    userId INTEGER NOT NULL,
                    date TEXT NOT NULL,
                    calories INTEGER NOT NULL,
                    protein INTEGER NOT NULL,
                    fat INTEGER NOT NULL,
                    carbs INTEGER NOT NULL
                );

                CREATE TABLE IF NOT EXISTS feelings (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    userId INTEGER NOT NULL,
                    date TEXT NOT NULL,
                    text TEXT NOT NULL
                );
            ";
            command.ExecuteNonQuery();
        }

        static void AddWorkout(long userId, string name, int minutes)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO workouts (userId, date, name, minutes)
                VALUES ($userId, $date, $name, $minutes);
            ";
            command.Parameters.AddWithValue("$userId", userId);
            command.Parameters.AddWithValue("$date", DateTime.Today.ToString("yyyy-MM-dd"));
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$minutes", minutes);
            command.ExecuteNonQuery();
        }

        static void SaveGoal(long userId, int goal)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO goals (userId, weeklyGoal)
                VALUES ($userId, $goal)
                ON CONFLICT(userId) DO UPDATE SET weeklyGoal = excluded.weeklyGoal;
            ";
            command.Parameters.AddWithValue("$userId", userId);
            command.Parameters.AddWithValue("$goal", goal);
            command.ExecuteNonQuery();
        }

        static void SaveFood(long userId, int calories, int protein, int fat, int carbs)
        {
            string today = DateTime.Today.ToString("yyyy-MM-dd");

            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var deleteCommand = connection.CreateCommand();
            deleteCommand.CommandText = @"
                DELETE FROM food
                WHERE userId = $userId AND date = $date;
            ";
            deleteCommand.Parameters.AddWithValue("$userId", userId);
            deleteCommand.Parameters.AddWithValue("$date", today);
            deleteCommand.ExecuteNonQuery();

            var insertCommand = connection.CreateCommand();
            insertCommand.CommandText = @"
                INSERT INTO food (userId, date, calories, protein, fat, carbs)
                VALUES ($userId, $date, $calories, $protein, $fat, $carbs);
            ";
            insertCommand.Parameters.AddWithValue("$userId", userId);
            insertCommand.Parameters.AddWithValue("$date", today);
            insertCommand.Parameters.AddWithValue("$calories", calories);
            insertCommand.Parameters.AddWithValue("$protein", protein);
            insertCommand.Parameters.AddWithValue("$fat", fat);
            insertCommand.Parameters.AddWithValue("$carbs", carbs);
            insertCommand.ExecuteNonQuery();
        }

        static void SaveFeeling(long userId, string feelingText)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO feelings (userId, date, text)
                VALUES ($userId, $date, $text);
            ";
            command.Parameters.AddWithValue("$userId", userId);
            command.Parameters.AddWithValue("$date", DateTime.Today.ToString("yyyy-MM-dd"));
            command.Parameters.AddWithValue("$text", feelingText);
            command.ExecuteNonQuery();
        }

        static string GetWorkoutReport(long userId)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT date, name, minutes
                FROM workouts
                WHERE userId = $userId
                ORDER BY date DESC, id DESC
                LIMIT 20;
            ";
            command.Parameters.AddWithValue("$userId", userId);

            using var reader = command.ExecuteReader();

            string report = "📊 Твои тренировки:\n\n";
            int i = 1;

            while (reader.Read())
            {
                string rawDate = reader.GetString(0);
                string name = reader.GetString(1);
                int minutes = reader.GetInt32(2);

                string dateText = DateTime.ParseExact(rawDate, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                    .ToString("dd.MM.yyyy");

                report += $"{i}) {dateText} — {name} — {minutes} мин\n";
                i++;
            }

            if (i == 1)
                return "У тебя пока нет тренировок.";

            return report;
        }

        static string GetStats(long userId)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            int totalWorkouts = 0;
            int totalMinutes = 0;
            int weeklyGoal = 0;
            int currentWeekWorkouts = 0;
            int currentWeekMinutes = 0;

            var totalCommand = connection.CreateCommand();
            totalCommand.CommandText = @"
                SELECT COUNT(*), COALESCE(SUM(minutes), 0)
                FROM workouts
                WHERE userId = $userId;
            ";
            totalCommand.Parameters.AddWithValue("$userId", userId);

            using (var reader = totalCommand.ExecuteReader())
            {
                if (reader.Read())
                {
                    totalWorkouts = reader.GetInt32(0);
                    totalMinutes = reader.GetInt32(1);
                }
            }

            var goalCommand = connection.CreateCommand();
            goalCommand.CommandText = @"
                SELECT weeklyGoal
                FROM goals
                WHERE userId = $userId;
            ";
            goalCommand.Parameters.AddWithValue("$userId", userId);

            var goalResult = goalCommand.ExecuteScalar();
            if (goalResult != null && goalResult != DBNull.Value)
                weeklyGoal = Convert.ToInt32(goalResult);

            DateTime today = DateTime.Today;
            int diff = ((int)today.DayOfWeek + 6) % 7;
            DateTime weekStart = today.AddDays(-diff);
            string weekStartText = weekStart.ToString("yyyy-MM-dd");

            var weekCommand = connection.CreateCommand();
            weekCommand.CommandText = @"
                SELECT COUNT(*), COALESCE(SUM(minutes), 0)
                FROM workouts
                WHERE userId = $userId AND date >= $weekStart;
            ";
            weekCommand.Parameters.AddWithValue("$userId", userId);
            weekCommand.Parameters.AddWithValue("$weekStart", weekStartText);

            using (var reader = weekCommand.ExecuteReader())
            {
                if (reader.Read())
                {
                    currentWeekWorkouts = reader.GetInt32(0);
                    currentWeekMinutes = reader.GetInt32(1);
                }
            }

            string foodText = "";
            var foodCommand = connection.CreateCommand();
            foodCommand.CommandText = @"
                SELECT calories, protein, fat, carbs
                FROM food
                WHERE userId = $userId AND date = $date;
                ";
            foodCommand.Parameters.AddWithValue("$userId", userId);
            foodCommand.Parameters.AddWithValue("$date", today.ToString("yyyy-MM-dd"));

            using (var reader = foodCommand.ExecuteReader())
            {
                if (reader.Read())
                {
                    foodText =
                        $"\n\n🍗 Сегодняшнее питание:\n" +
                        $"Калории: {reader.GetInt32(0)}\n" +
                        $"Белки: {reader.GetInt32(1)}\n" +
                        $"Жиры: {reader.GetInt32(2)}\n" +
                        $"Углеводы: {reader.GetInt32(3)}";
                }
            }

            string stats =
                "📈 Статистика:\n\n" +
                $"Всего тренировок: {totalWorkouts}\n" +
                $"Всего минут: {totalMinutes}\n\n" +
                $"За эту неделю тренировок: {currentWeekWorkouts}\n" +
                $"За эту неделю минут: {currentWeekMinutes}";

            if (weeklyGoal > 0)
                stats += $"\nЦель на неделю: {weeklyGoal}\nПрогресс: {currentWeekWorkouts}/{weeklyGoal}";

            stats += foodText;

            return stats;
        }

        static int GetWorkoutStreak(long userId)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT DISTINCT date
                FROM workouts
                WHERE userId = $userId
                ORDER BY date DESC;
            ";
            command.Parameters.AddWithValue("$userId", userId);

            using var reader = command.ExecuteReader();

            List<DateTime> dates = new();

            while (reader.Read())
            {
                string rawDate = reader.GetString(0);
                if (DateTime.TryParseExact(rawDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d))
                    dates.Add(d.Date);
            }

            if (dates.Count == 0)
                return 0;

            int streak = 0;
            DateTime current = DateTime.Today;

            foreach (var d in dates)
            {
                if (d == current)
                {
                    streak++;
                    current = current.AddDays(-1);
                }
                else if (d == current.AddDays(1))
                {
                    continue;
                }
                else
                {
                    break;
                }
            }

            return streak;
        }

        static string GetAchievements(long userId)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            int totalWorkouts = 0;

            var countCommand = connection.CreateCommand();
            countCommand.CommandText = @"
                SELECT COUNT(*)
                FROM workouts
                WHERE userId = $userId;
            ";
            countCommand.Parameters.AddWithValue("$userId", userId);

            var countResult = countCommand.ExecuteScalar();
            if (countResult != null && countResult != DBNull.Value)
                totalWorkouts = Convert.ToInt32(countResult);

            int streak = GetWorkoutStreak(userId);

            bool hasFoodToday = false;
            var foodCommand = connection.CreateCommand();
            foodCommand.CommandText = @"
                SELECT COUNT(*)
                FROM food
                WHERE userId = $userId AND date = $date;
            ";
            foodCommand.Parameters.AddWithValue("$userId", userId);
            foodCommand.Parameters.AddWithValue("$date", DateTime.Today.ToString("yyyy-MM-dd"));

            var foodResult = foodCommand.ExecuteScalar();
            if (foodResult != null && Convert.ToInt32(foodResult) > 0)
                hasFoodToday = true;

            string result = "🏆 Достижения:\n\n";
            bool any = false;

            if (totalWorkouts >= 1) { result += "✅ Первая тренировка\n"; any = true; }
            if (totalWorkouts >= 5) { result += "💪 5 тренировок\n"; any = true; }
            if (totalWorkouts >= 10) { result += "🔥 10 тренировок\n"; any = true; }
            if (totalWorkouts >= 25) { result += "🚀 25 тренировок\n"; any = true; }
            if (streak >= 3) { result += "🔥 Стрик 3 дня\n"; any = true; }
            if (streak >= 7) { result += "🌋 Стрик 7 дней\n"; any = true; }
            if (hasFoodToday) { result += "🍗 Питание записано сегодня\n"; any = true; }

            if (!any)
                return "Пока достижений нет. Начни с первой тренировки 💪";

            return result;
        }

        static bool DeleteLastWorkout(long userId)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var getCommand = connection.CreateCommand();
            getCommand.CommandText = @"
                SELECT id
                FROM workouts
                WHERE userId = $userId
                ORDER BY date DESC, id DESC
                LIMIT 1;
            ";
            getCommand.Parameters.AddWithValue("$userId", userId);

            var result = getCommand.ExecuteScalar();

            if (result == null || result == DBNull.Value)
                return false;

            long id = (long)result;

            var deleteCommand = connection.CreateCommand();
            deleteCommand.CommandText = @"
                DELETE FROM workouts
                WHERE id = $id;
            ";
            deleteCommand.Parameters.AddWithValue("$id", id);
            deleteCommand.ExecuteNonQuery();

            return true;
        }
    }
}