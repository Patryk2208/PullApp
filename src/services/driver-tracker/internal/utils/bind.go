package utils

import (
	"encoding/json"

	"github.com/gin-gonic/gin"
	"github.com/gin-gonic/gin/binding"
)

// BindStrictJSON decodes JSON and rejects unknown fields, then runs binding validation tags.
func BindStrictJSON[T any](c *gin.Context, dst *T) error {
	dec := json.NewDecoder(c.Request.Body)
	dec.DisallowUnknownFields()
	if err := dec.Decode(dst); err != nil {
		return err
	}
	return binding.Validator.ValidateStruct(dst)
}