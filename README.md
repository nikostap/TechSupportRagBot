# Class-Engineering Support

ASP.NET Core Razor Pages система техподдержки с ролями, чатами, RAG, Qdrant и PostgreSQL.

## Локальный Docker-запуск

```bash
cp .env.example .env
docker compose up -d --build
```

Адрес:

```text
http://localhost:5028
```

Стартовый администратор:

```text
Логин: admin
Пароль: Admin123!
```

## Основные сервисы

- `app` — ASP.NET Core приложение.
- `postgres` — основная PostgreSQL БД.
- `qdrant` — векторная БД.
- `ollama` — локальная LLM, запускается профилем `ollama`.

Для запуска Ollama-контейнера:

```bash
docker compose --profile ollama up -d ollama
docker exec -it techsupportragbot-ollama-1 ollama pull llama3.1
docker exec -it techsupportragbot-ollama-1 ollama pull embeddinggemma
```

## Env-настройки

Секреты не хранятся в репозитории. Используйте `.env` локально или переменные окружения на VDS.

PostgreSQL:

```text
ConnectionStrings__DefaultConnection=Host=postgres;Port=5432;Database=techsupport;Username=techsupport;Password=...
Database__Provider=Postgres
```

LLM/RAG:

```text
Rag__ChatProvider=Ollama|OpenAI|DeepSeek|AiTunnel
Rag__EmbeddingProvider=Ollama|OpenAI|Qwen|AiTunnel
Rag__OllamaBaseUrl=http://ollama:11434
Rag__QdrantBaseUrl=http://qdrant:6333
```

OpenAI:

```text
OpenAI__ApiKey=...
OpenAI__ChatModel=gpt-4.1-mini
OpenAI__EmbeddingModel=text-embedding-3-small
```

DeepSeek:

```text
DeepSeek__ApiKey=...
DeepSeek__ChatModel=deepseek-chat
```

DeepSeek используется для chat completions. Для embeddings используйте `Ollama` или `OpenAI`.

Qwen Model Studio используется для embeddings размерности 1024 через OpenAI-compatible API:

```env
Qwen__BaseUrl=https://ws-am1n1jqyhug10mfy.ap-southeast-1.maas.aliyuncs.com/compatible-mode/v1
Qwen__ApiKey=...
Qwen__EmbeddingModel=text-embedding-v4
Qwen__EmbeddingDimensions=1024
```

После выбора Qwen коллекция Qdrant с другой размерностью автоматически пересоздаётся и переиндексируется.

AITunnel поддерживается одновременно для LLM и embeddings через OpenAI-compatible API:

```env
AiTunnel__BaseUrl=https://api.aitunnel.ru/v1
AiTunnel__ApiKey=...
AiTunnel__ChatModel=auto
AiTunnel__EmbeddingModel=text-embedding-v4
```

## VDS / Candy Docker

1. Создайте приложение из GitHub-репозитория.
2. Укажите Docker Compose deploy из `docker-compose.yml`.
3. Создайте env-переменные по `.env.example`.
4. Для production замените:
   - `POSTGRES_PASSWORD`
   - `ConnectionStrings__DefaultConnection`
   - SMTP-настройки
   - API-ключи OpenAI/DeepSeek при необходимости.
5. Пробросьте наружу порт приложения `5028` или настройте reverse proxy на внутренний порт `8080`.

## Проверка

```bash
docker compose ps
curl http://localhost:5028
docker compose logs --tail=100 app
```
