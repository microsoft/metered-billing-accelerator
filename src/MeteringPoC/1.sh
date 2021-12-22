#!/bin/bash

./show_meters.sh 1 | jq -r '(["Dimension","Consumption"] | (.,map(length*"-"))), (.meters["fdc778a6-1281-40e4-cade-4a5fc11f5440"].currentMeters[] | [.dimensionId,.meterValue.consumed.consumedQuantity]) | @tsv'
