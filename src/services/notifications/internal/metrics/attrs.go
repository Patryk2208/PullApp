package metrics

import "go.opentelemetry.io/otel/attribute"

func channelAttr(v string) attribute.KeyValue   { return attribute.String("channel", v) }
func statusAttr(v string) attribute.KeyValue    { return attribute.String("status", v) }
func eventTypeAttr(v string) attribute.KeyValue { return attribute.String("event_type", v) }
