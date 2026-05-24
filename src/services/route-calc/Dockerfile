FROM python:3.13-slim

ENV PYTHONUNBUFFERED=1
ENV POETRY_VIRTUALENVS_CREATE=false
ENV POETRY_VERSION=2.1.4

WORKDIR /app

RUN apt-get update && apt-get install -y \
    curl \
    build-essential \
    cmake \
    libcurl4 \
    libcurl4-openssl-dev \
    && rm -rf /var/lib/apt/lists/*

RUN pip install "poetry==$POETRY_VERSION"
COPY pyproject.toml poetry.lock* ./

RUN poetry install --no-interaction --no-ansi --no-root

COPY route_calc/ ./route_calc/

RUN mkdir -p /app/config && cp route_calc/config.json /app/config/config.json

COPY CMakeLists.txt .
COPY cpp .

RUN pip install . --no-cache-dir \
    && rm -rf /app/route_calc

CMD ["python", "-m", "route_calc.main"]
