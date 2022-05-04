#/bin/bash

basedir="$( dirname "$( readlink -f "$0" )" )"

AZURE_SAAS_DEMO_SETTINGS="${basedir}/settings.json"

if [[ ! -f "${AZURE_SAAS_DEMO_SETTINGS}" ]]; then
    echo "{}" > "${AZURE_SAAS_DEMO_SETTINGS}" ; 
fi

function put-value { 
    local key="$1" ;
    local variableValue="$2" ;
    local json ;
    json="$( cat "${AZURE_SAAS_DEMO_SETTINGS}" )" ;
    echo "${json}" \
       | jq --arg x "${variableValue}" ".${key}=(\$x)" \
       > "${AZURE_SAAS_DEMO_SETTINGS}"
}

put-value "$1" "$2"
