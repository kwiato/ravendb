/// <reference path="../../../../typings/tsd.d.ts"/>

import document = require("models/database/documents/document");

class patchDocument extends document {

    static readonly allPatchOptions: Array<patchOption> = ["Document", "Collection", "Index"];

    patchOnOption = ko.observable<patchOption>();
    selectedItem = ko.observable<string>();
    query = ko.observable<string>();
    script = ko.observable<string>();

    patchAll = ko.observable<boolean>(false);

    selectedIndex: KnockoutComputed<string>;

    constructor(dto: patchDto) {
        super(dto);
        this.patchOnOption(dto.PatchOnOption);
        this.query(dto.Query);
        this.script(dto.Script);
        this.selectedItem(dto.SelectedItem);

        this.selectedIndex = ko.pureComputed(() => {
            return this.patchOnOption() === "Index" ? this.selectedItem() : null;
        });
    }

    static empty() {
        const meta: any = {};
        meta['@collection'] = 'PatchDocuments';
        return new patchDocument({
            '@metadata': meta,
            PatchOnOption: "Document",
            Query: "",
            Script: "",
            SelectedItem: "",
            Values: []
        });
    }

    toDto(): patchDto {
        const meta = this.__metadata.toDto();
        return {
            '@metadata': meta,
            PatchOnOption: this.patchOnOption(),
            Query: this.query(),
            Script: this.script(),
            SelectedItem: this.selectedItem()
        };
    }

    /* TODO:
    name(): string {
        return this.__metadata.id.replace('Studio/Patch/', '');
    }

    resetMetadata() {
        this.__metadata = new documentMetadata();
        this.__metadata.collection = 'PatchDocuments';
    }

    clone() {
        return new patchDocument(this.toDto());
    }*/
}

export = patchDocument;
