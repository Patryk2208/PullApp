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

COPY schemas/ ./schemas/
COPY route_calc/ ./route_calc/

RUN mkdir -p route_calc/generated && \
    protoc --proto_path=schemas/pullapp/core/v1/ \
           --python_out=route_calc/generated/ \
           schemas/pullapp/core/v1/queue.proto && \
    touch route_calc/generated/__init__.py

COPY CMakeLists.txt .
COPY cpp/ ./cpp/

RUN poetry build

RUN pip install --no-cache-dir dist/*.whl

CMD ["python", "-m", "route_calc.main"]
