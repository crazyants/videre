﻿videre.registerNamespace('videre.widgets');
videre.registerNamespace('videre.widgets.admin');

videre.widgets.admin.file = videre.widgets.base.extend(
{
    //constructor
    init: function()
    {
        this._base();  //call base method
        this._tree = null;
        this._data = null;
        this._dataDict = null;
        this._treeData = null;
        this._itemData = null;
        this._itemDataDict = null;
        this._dropDataDict = {};

        this._selectedKey = null;
        this._selectedItem = null;

        this._itemListCtr = null;
        this._dialog = null;
        this._uniqueName = null;

        this._delegates = {
            onDataReturn: videre.createDelegate(this, this._onDataReturn),
            onSaveReturn: videre.createDelegate(this, this._onSaveReturn),
            onNodeSelected: videre.createDelegate(this, this._onNodeSelected),
            onActionClicked: videre.createDelegate(this, this._onActionClicked)
        };
    },

    _onLoad: function(src, args)
    {
        this._base(); //call base

        this._tree = this.getControl('Tree').dynatree(
        {
            minExpandLevel: 2,
            onActivate: this._delegates.onNodeSelected
        });
        this._itemListCtr = this.getControl('ItemListCtr').hide();
        this._dialog = this.getControl('Dialog').modal('hide');
        this.getControl('btnNew').click(videre.createDelegate(this, this._onNewClicked));
        this.getControl('btnSave').click(videre.createDelegate(this, this._onSaveClicked));

        var self = this;
        var myDropzone = new Dropzone('#' + this.getId('ItemTable'),
            {
                paramName: 'qqfile',
                maxFilesize: 2, // MB
                url: videre.resolveUrl('~/core/file/upload'),
                uploadMultiple: true,
                previewsContainer: '#' + this.getId('DropZoneContainer'),
                sending: function(file) { self.lock(); },
                success: videre.createDelegate(this, this._onDropFileComplete),
                complete: videre.createDelegate(this, this._onDropComplete)
            });

        var uploader = new qq.FileUploaderBasic({
            button: this.getControl('btnUpload')[0],
            params: {},
            action: videre.resolveUrl('~/core/file/upload'),
            onSubmit: videre.createDelegate(this, this._onFileSubmit),
            onComplete: videre.createDelegate(this, this._onFileUploadReturn),
            showMessage: videre.createDelegate(this, this._onFileMessage),
            debug: true
        });

        this.refresh();
    },

    refresh: function()
    {
        this.ajax('~/core/File/Get', {}, this._delegates.onDataReturn);
    },

    bindTree: function()
    {
        this._tree.dynatree("getRoot").removeChildren();
        this._tree.dynatree("getRoot").append(this._treeData);
        this._tree.find('.dynatree-container').height(this._tree.innerHeight() - 8).css('margin', 0);   //todo: better way?

        if (this._selectedKey != null)
        {
            var node = (this._tree.dynatree('getTree').activateKey(this._selectedKey));
            if (node == null)
            {
                this._itemData = null;
                this.bindItems(this._selectedKey);
            }
        }
    },

    bindItems: function(key)
    {
        var items = this._data.where(function(d) { return d.treeKey.indexOf(key) == 0; }).orderBy(function(d) { return d.Key; }); 
        if (items.length > 0)
        {
            this._itemDataDict = items.toDictionary(function(d) { return d.Id; });

            this._itemListCtr.show();
            videre.dataTables.clear(this.getControl('ItemTable'));
            this.getControl('ItemList').html(this.getControl('ItemListTemplate').render(items));
            this.getControl('ItemList').find('.btn').click(this._delegates.onActionClicked);
            videre.dataTables.bind(this.getControl('ItemTable'), { aoColumns: [{ bSortable: false }] });
        }
        else
            this._itemListCtr.hide();
    },

    newItem: function()
    {
        this._selectedItem = this._getNewFile();
        this.edit();
    },

    edit: function()
    {
        if (this._selectedItem != null)
        {
            //this.getControl('txtFileName').val('');
            this.bindData(this._selectedItem);
            this._uniqueName = null;

            videre.UI.showModal(this._dialog);
            this.clearMsgs(this._dialog);
        }
        else
            this._dialog.modal('hide');
    },

    save: function()
    {
        if (this._selectedItem != null)
        {
            if (this.validControls(this._dialog, this._dialog))
            {
                var file = this.persistData(this._selectedItem, null, null, true);
                //file.Urls = this.getControl('txtUrls').val().split('\r\n');
                this.ajax('~/core/File/Save', { file: file, uniqueName: this._uniqueName }, this._delegates.onSaveReturn, null, this._dialog);
            }
        }
    },

    deleteItem: function(id)
    {
        //if (confirm('Are you sure you wish to remove this entry?'))
        var self = this;
        videre.UI.confirm('Delete Entry', 'Are you sure you wish to remove this entry?', function ()
        {
            self.ajax('~/core/File/Delete', { id: id }, self._delegates.onSaveReturn);
        });            
    },

    handleAction: function(action, id)
    {
        this._selectedItem = this._itemDataDict[id];
        if (this._selectedItem != null)
        {
            if (action == 'edit')
                this.edit();
            else if (action == 'delete')
                this.deleteItem(id);
        }
    },

    _getNewFile: function()
    {
        return { Id: '', PortalId: '', MimeType: '', Url: '', Size: null };
    },

    _onNodeSelected: function(node)
    {
        this._selectedKey = node.data.key;
        this.bindItems(this._selectedKey);
    },

    _onDataReturn: function(result, ctx)
    {
        if (!result.HasError)
        {
            this._data = result.Data.select(function(d)
            {
                d.treeKey = d.Url;
                d.imageUrl = (d.MimeType.indexOf('image/') > -1 ? videre.resolveUrl('~/Core/f/' + d.RenderUrl) : videre.resolveUrl('~/content/images/document.png'));
                return d; 
            });
            this._treeData = videre.tree.getTreeData('root', this._data, function(d) { return d.treeKey; });
            this.bindTree();
        }
    },

    _onSaveReturn: function(result)
    {
        if (!result.HasError && result.Data)
        {
            this._dialog.modal('hide');
            this.refresh();
        }
    },

    _onActionClicked: function(e)
    {
        var ctl = $(e.target).closest('[data-action]');
        this.handleAction(ctl.data('action'), ctl.data('id'));
    },

    _onNewClicked: function(e)
    {
        this.newItem();
    },

    _onSaveClicked: function(e)
    {
        this.save();
    },

    _onTypeChanged: function(e)
    {
        this.refresh();
    },

    _onFileSubmit: function(id, fileName)
    {
        this.lock(this.getControl('Dialog'));
    },

    _onFileUploadReturn: function(id, fileName, result)
    {
        this.unlock(this.getControl('Dialog'));
        if (!result.HasError)
        {
            this._uniqueName = result.Data[0].UniqueName;
            
            this.bindData(result.Data[0], this.getControl('Dialog').find('.file-upload-detail'));
            //this.getControl('txtMimeType').val(result.Data.mimeType);
            //this.getControl('txtFileName').val(result.Data.fileName);
            //this.getControl('txtFileSize').val(result.Data.fileSize);
        }
        this.addMsgs(result.Messages, this.getControl('Dialog'));
    },

    _onFileMessage: function(message)
    {
        this.addMsg('FileMessage', message, true, this.getControl('Dialog'));   //todo: test!
    },

    _onDropFileComplete: function(file, response)
    {
        var result = videre.deserialize(response);
        if (!result.HasError)
            this._dropDataDict = result.Data.toDictionary(function(d) { return d.FileName; });

        this.addMsgs(result.Messages);
    },

    _onDropComplete: function(file)
    {
        this.unlock();
        if (this._dropDataDict[file.name] != null)
        {
            var data = this._dropDataDict[file.name];
            var newFile = this._getNewFile();
            var url = data.FileName;
            if (!String.isNullOrEmpty(this._selectedKey))
                url = this._selectedKey + '/' + url;
            newFile.Url = url;
            newFile.Size = data.Size;
            newFile.MimeType = data.MimeType;
            this.ajax('~/core/File/Save', { file: newFile, uniqueName: data.UniqueName }, this._delegates.onSaveReturn);
        }
        this._dropDataDict[file.name] != null;
    }



});

