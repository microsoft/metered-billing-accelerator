#!/bin/bash

./show_meters.sh 1 | jq -r '(["Updated","Dimension","Consumption"] | (.,map(length*"-"))), (.meters["fdc778a6-1281-40e4-cade-4a5fc11f5440"].currentMeters[] | [.meterValue.consumed.lastUpdate,.dimensionId,.meterValue.consumed.consumedQuantity]) | @tsv'
