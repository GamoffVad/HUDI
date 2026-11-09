# Настройки провайдеров

Начиная с HUDI 7.3.10 в пункте «Другой сервер» можно указать HTTP-адрес корпоративного OpenAI-совместимого API, например `http://llm.corp:8080/v1` или `http://10.42.0.8:8000/v1`. Поддерживается IPv6. Если сервер не требует ключа, выключите соответствующий переключатель. Привязка сохранённого ключа учитывает схему, имя сервера и порт; при их изменении ключ нужно ввести для нового адреса.

Значения сверены с официальной документацией 7 ноября 2026 года. Пресеты можно редактировать; доступность моделей зависит от аккаунта. Ключи пользователь вводит сам.

| Провайдер | Адрес API | Начальная модель | Формат |
|---|---|---|---|
| OpenAI | `https://api.openai.com/v1` | `gpt-5.4-mini` | Responses |
| Anthropic · Claude | `https://api.anthropic.com/v1` | `claude-haiku-5-5` | Chat Completions |
| Google · Gemini | `https://generativelanguage.googleapis.com/v1beta/openai` | `gemini-3.8-flash` | Chat Completions |
| DeepSeek | `https://api.deepseek.com` | `deepseek-flash` | Chat Completions |
| OpenRouter | `https://openrouter.ai/api/v1` | `openrouter/auto` | Chat Completions |
| xAI · Grok | `https://api.x.ai/v1` | `grok-4.7` | Responses |
| Groq | `https://api.groq.com/openai/v1` | `llama-3.3-70b-versatile` | Chat Completions |
| Mistral AI | `https://api.mistral.ai/v1` | `mistral-small-latest` | Chat Completions |
| Together AI | `https://api.together.ai/v1` | `MiniMaxAI/MiniMax-M3` | Chat Completions |
| Ollama | `http://localhost:11434/v1` | Выбрать установленную | Chat Completions |
| LM Studio | `http://localhost:1234/v1` | Выбрать установленную | Chat Completions |
| Другой сервер | Ввести свой | Ввести свою | Chat Completions |

Выбор пресета меняет адрес, модель, формат API, лимит, тайм-аут, температуру и её отправку, параметр лимита, поток и необходимость ключа. Системная инструкция сохраняется. При открытии окна ранее сохранённые параметры не сбрасываются. Старый ключ не пересылается новому сервису.

Лимит пресетов — 4096 токенов. Большинство оставляет температуру на стороне модели; для Groq, Mistral и Grok отправляется 0,7. Тайм-аут — 120 секунд; DeepSeek и Together — 180, локальные серверы — 300, Grok — 900. Это начальные настройки приложения, а не единые заводские значения всех сервисов.

У локального сервера выберите реально установленную модель; по умолчанию ключ выключен. Для Claude используется ключ Claude API, привязанный к рабочему пространству. Организационные ключи без workspace требуют дополнительной настройки у провайдера. Список моделей Claude запрашивается с нативными заголовками, чат — через официальный слой совместимости. Together возвращает список моделей массивом без обёртки `data`.

Протокол и заполнение полей проверены на тестовом HTTP-обработчике без личных ключей и платных запросов. Реальная доступность конкретного аккаунта требует его ключа.

Официальные источники:

- [OpenAI: GPT-5.4 mini](https://developers.openai.com/api/docs/models/gpt-5.4-mini)
- [Claude: модели](https://platform.claude.com/docs/en/about-claude/models/overview), [совместимость](https://platform.claude.com/docs/en/cli-sdks-libraries/libraries/openai-sdk)
- [Gemini](https://ai.google.dev/gemini-api/docs/openai)
- [DeepSeek](https://api-docs.deepseek.com/)
- [OpenRouter](https://openrouter.ai/blog/announcements/auto-and-auto-code-routers/)
- [Grok](https://docs.x.ai/developers/model-capabilities/text/best-practices)
- [Groq: API](https://console.groq.com/docs/openai), [модели](https://console.groq.com/docs/models)
- [Mistral](https://docs.mistral.ai/resources/migration-guides)
- [Together](https://docs.together.ai/docs/inference/openai-compatibility)
- [Ollama](https://docs.ollama.com/api/openai-compatibility)
- [LM Studio](https://lmstudio.ai/docs/developer/openai-compat)
