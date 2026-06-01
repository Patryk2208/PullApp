PROTO_SRC  := ../../schemas/pullapp/core/v1/queue.proto
PROTO_OUT  := route_calc/generated

.PHONY: proto build dev test test-db clean

proto:
	mkdir -p $(PROTO_OUT)
	protoc --proto_path=../../schemas/pullapp/core/v1/ \
	       --python_out=$(PROTO_OUT)/ \
	       $(PROTO_SRC)

build:
	pip install -e .

dev: proto build
	poetry install

test:
	poetry run pytest tests/ \
	  --ignore=tests/rabbitmq_e2e_test.py \
	  --ignore=tests/rabbitmq_integration_test.py \
	  --ignore=tests/osrm_integration_test.py \
	  --ignore=tests/db_test.py \
	  -v

test-db:
	poetry run pytest tests/db_test.py -v

clean:
	rm -f route_calc/algorithms/*.so route_calc/algorithms/*.pyd
	rm -rf build/ dist/ *.egg-info/