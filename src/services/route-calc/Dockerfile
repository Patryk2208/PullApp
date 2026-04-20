FROM python:3.13-slim

ENV PYTHONUNBUFFERED=1
ENV POETRY_VIRTUALENVS_CREATE=false
ENV POETRY_VERSION=2.1.4

WORKDIR /app

RUN apt-get update && apt-get install -y curl build-essential && rm -rf /var/lib/apt/lists/*

RUN pip install "poetry==$POETRY_VERSION"
COPY pyproject.toml poetry.lock* ./
COPY route_calc/ ./route_calc/

RUN poetry install --no-interaction

CMD ["python", "-m", "route_calc.main"]