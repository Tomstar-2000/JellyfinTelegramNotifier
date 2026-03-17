# Jellyfin Telegram Notifier Plugin

[![Jellyfin](https://img.shields.io/badge/Jellyfin-10.11.0+-00a4dc.svg)](https://jellyfin.org)

A feature-rich Jellyfin plugin that sends rich Telegram notifications for new media additions. It features smart season aggregation, customisable templates, and a manual retrigger panel with image previews.

## ✨ Features

- **Rich Notifications**: Sends `sendPhoto` messages with high-quality cover art.
- **Smart Aggregation**: Automatically groups episode additions from the same season into a single notification to avoid spam.
- **Customisable Templates**: Fully control the message format using tokens like `{Title}`, `{Year}`, `{Overview}`, `{SeriesName}`, `{SeasonNumber}`, `{EpisodeNumber}`, etc.
- **Manual Retrigger Panel**: A dedicated UI within Jellyfin to resend notifications for past events, including a searchable image dropdown and thumbnail previews.
- **TVDB Integration**: Optional TVDB API support for enhanced season completeness checks.
- **Topic Support**: Send notifications to different Telegram topics (Movies/Seasons/Episodes).
- **Recent Events Log**: Tracks past notifications for easy management and retriggering.

## 🚀 Installation

1. Create a folder named `Jellyfin.Plugin.TelegramNotifier` in your Jellyfin `plugins` directory.
2. Download the latest release `Jellyfin.Plugin.TelegramNotifier.dll` and `manifest.json`.
3. Place them in the created folder.
4. Restart Jellyfin.

## 🛠️ Configuration

After installation, go to **Dashboard > Plugins > Telegram Notifier** to configure:

### 1. Telegram Bot Setup

- Create a bot via [@BotFather](https://t.me/BotFather) to get your **Bot Token**.
- Get your **Chat ID** (or Topic ID if using a group with topics).
- Enter your public-facing **Jellyfin Server URL** for link generation.

### 2. Message Templates

Customise exactly how your notifications look.

- **Movie Template**: e.g., `🎬 *{Title}* ({Year})\n\n{Overview}`
- **Season Template**: e.g., `📺 *{SeriesName}* – Season {SeasonNumber} is now complete!`
- **Episode Template**: e.g., `📺 *{SeriesName}* S{SeasonNumber}E{EpisodeNumber} – *{EpisodeTitle}*`

### 3. Advanced Features

- **Season Threshold**: Set the percentage of episodes required to trigger a season-level notification.
- **TVDB Support**: Enable for more accurate season metadata fetching.
- **Aggregation Delay**: Delay notifications slightly to allow for batch additions.

## 🧪 Manual Retrigger

Need to resend a notification? Use the "Recent Events" tab in the plugin configuration. You can:

- View a list of recently added items.
- Search for the perfect thumbnail using the integrated image dropdown.
- Edit the description before resending.
- Delete entries from the log.

---

**Developed by Tomstar2000**
