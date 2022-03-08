#!/bin/bash


./show_meters.sh 1 | jq -r '.meters["fdc778a6-1281-40e4-cade-4a5fc11f5440"].currentMeters'
