package model

import "encoding/json"

type Notification struct {
	Type    string
	Payload json.RawMessage
}