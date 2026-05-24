FROM python:3.13-slim

ENV PYTHONUNBUFFERED=1
ENV POETRY_VIRTUALENVS_CREATE=false
ENV POETRY_VERSION=2.1.4

WORKDIR /app

RUN apt-get update && apt-get install -y \
    curl \
    build-essential \
    cmake \
    libcurl4-openssl-dev \
    && rm -rf /var/lib/apt/lists/*

RUN pip install "poetry==$POETRY_VERSION"
COPY pyproject.toml poetry.lock* ./

RUN poetry install --no-interaction --no-ansi --no-root

COPY route_calc/ ./route_calc/

COPY CMakeLists.txt .
COPY cpp .

RUN poetry build

COPY dist/ ./dist/

RUN pip install --no-cache-dir dist/*.whl

CMD ["python", "-m", "route_calc.main"]
