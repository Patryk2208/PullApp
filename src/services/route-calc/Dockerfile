FROM python:3.13-slim AS base

ENV PYTHONUNBUFFERED=1
ENV POETRY_VERSION=2.1.4

WORKDIR /app

FROM base AS builder

RUN apt-get update && apt-get install -y curl build-essential && rm -rf /var/lib/apt/lists/*

RUN pip install "poetry==$POETRY_VERSION"

COPY pyproject.toml poetry.lock* ./

RUN poetry config virtualenvs.create false && poetry install --no-interaction --no-ansi --no-root

FROM base AS runtime

COPY --from=builder /usr/local/lib/python3.13 /usr/local/lib/python3.13
COPY --from=builder /usr/local/bin /usr/local/bin

COPY . .

CMD ["python", "main.py"]