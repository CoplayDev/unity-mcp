FROM python:3.13-slim

RUN apt-get update && apt-get install -y --no-install-recommends \
    git \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

RUN pip install uv

COPY . /app

RUN uv sync

CMD ["uv", "run", "server.py"]
