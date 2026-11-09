// Synthetic profile generated inside an isolated container, with no operator input.
package main

import (
	"os"
	"runtime/pprof"
)

func main() {
	if len(os.Args) != 2 {
		panic("one explicit output path is required")
	}
	out, err := os.Create(os.Args[1])
	if err != nil {
		panic(err)
	}
	defer out.Close()
	if err = pprof.Lookup("goroutine").WriteTo(out, 0); err != nil {
		panic(err)
	}
}
